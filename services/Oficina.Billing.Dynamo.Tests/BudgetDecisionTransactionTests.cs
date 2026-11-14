using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Dynamo.Tests;

public sealed class BudgetDecisionTransactionTests
{
    [Theory]
    [InlineData(true, "BudgetApproved", BudgetStatus.Approved)]
    [InlineData(false, "BudgetRefused", BudgetStatus.Refused)]
    public async Task DecisionWritesStatusAndEventAtomicallyAndIgnoresRetry(
        bool approved, string eventType, BudgetStatus expectedStatus)
    {
        var endpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para o teste local.");
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = endpoint });
        var table = "billing_decision_test_" + Guid.NewGuid().ToString("N");
        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName = table,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
            KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
        });
        var settings = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DynamoDb:Table"] = table }).Build();
        var repository = new BillingRepository(dynamo, settings);
        var budget = Budget.Create(Guid.NewGuid(), 1, "sp-centro",
            [new BudgetLine(Guid.NewGuid(), "Revisão", 1, 12500)]);
        await repository.CreateAsync(budget, CancellationToken.None);

        var first = await repository.DecideAsync(budget.WorkOrderId, 1, approved, CancellationToken.None);
        var retry = await repository.DecideAsync(budget.WorkOrderId, 1, approved, CancellationToken.None);
        Assert.Equal(expectedStatus, first!.Status);
        Assert.Equal(expectedStatus, retry!.Status);
        Assert.Equal(expectedStatus,
            (await repository.GetAsync(budget.WorkOrderId, 1, CancellationToken.None))!.Status);
        var pending = Assert.Single(await repository.GetPendingOutboxAsync(CancellationToken.None));
        using var json = JsonDocument.Parse(pending.Body);
        Assert.Equal(eventType, json.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(budget.Id, json.RootElement.GetProperty("payload").GetProperty("budgetId").GetGuid());
        Assert.Equal(pending.EventId,
            json.RootElement.GetProperty("payload").GetProperty("decisionId").GetGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.DecideAsync(budget.WorkOrderId, 1, !approved, CancellationToken.None));
        Assert.Single(await repository.GetPendingOutboxAsync(CancellationToken.None));
    }
}
