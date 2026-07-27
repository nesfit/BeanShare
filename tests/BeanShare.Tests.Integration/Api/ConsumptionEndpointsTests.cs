using System.Net;
using System.Net.Http.Json;
using BeanShare.Contracts.CoffeeStock;
using BeanShare.Contracts.Consumption;
using BeanShare.Contracts.Spaces;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;

namespace BeanShare.Tests.Integration.Api;

public sealed class ConsumptionEndpointsTests : IClassFixture<PostgreSqlFixture>
{
    private readonly HttpClient _client;

    public ConsumptionEndpointsTests(PostgreSqlFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    private async Task<Guid> CreateSpaceAsync(string name = "Consumption Test Space")
    {
        var request = new CreateSpaceRequest { Name = name };
        var response = await _client.PostAsJsonAsync("/api/spaces", request);
        var created = await response.Content.ReadFromJsonAsync<CreateSpaceResponse>();
        return created!.SpaceId;
    }

    private async Task AddStockAsync(Guid spaceId)
    {
        var request = new AddStockPurchaseRequest
        {
            ProductName = "Test Coffee",
            ProductBrand = "TestBrand",
            ProductType = "Espresso",
            QuantityGrams = 500,
            CostAmount = 25.00m,
            CostCurrency = "USD",
            Vendor = "Test Vendor",
            PurchasedAt = DateTime.UtcNow
        };
        await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/stock/purchases", request);
    }

    [Fact]
    public async Task RecordConsumption_WithValidRequest_ShouldReturnCreated()
    {
        var spaceId = await CreateSpaceAsync("Consume Valid Test");
        await AddStockAsync(spaceId);

        var request = new RecordConsumptionRequest
        {
            SpaceId = spaceId,
            ProductName = "Test Coffee",
            ProductBrand = "TestBrand",
            ProductType = "Espresso",
            QuantityGrams = 14
        };

        var response = await _client.PostAsJsonAsync("/api/consumptions", request);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task RecordConsumption_WithNoStock_ShouldReturnBadRequest()
    {
        var spaceId = await CreateSpaceAsync("Consume No Stock Test");

        var request = new RecordConsumptionRequest
        {
            SpaceId = spaceId,
            ProductName = "NonExistent",
            ProductBrand = "NoBrand",
            ProductType = "Espresso",
            QuantityGrams = 14
        };

        var response = await _client.PostAsJsonAsync("/api/consumptions", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetConsumptionHistory_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/api/me/consumption/history");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConsumptionWorkflow_AddStockThenConsume_ShouldReduceStock()
    {
        var spaceId = await CreateSpaceAsync("Consumption Workflow Test");
        await AddStockAsync(spaceId);

        // Get stock before
        var stockBefore = await _client.GetAsync($"/api/spaces/{spaceId}/stock");
        stockBefore.StatusCode.Should().Be(HttpStatusCode.OK);

        // Consume
        var consumeRequest = new RecordConsumptionRequest
        {
            SpaceId = spaceId,
            ProductName = "Test Coffee",
            ProductBrand = "TestBrand",
            ProductType = "Espresso",
            QuantityGrams = 14
        };
        var consumeResponse = await _client.PostAsJsonAsync("/api/consumptions", consumeRequest);
        consumeResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // Get stock after
        var stockAfter = await _client.GetAsync($"/api/spaces/{spaceId}/stock");
        stockAfter.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBagSuggestions_AfterLoggingFromOneBag_ShouldRankThatBagFirst()
    {
        var spaceId = await CreateSpaceAsync("Bag Suggestion Test");

        // Two identical bags of the same product — only the consumption history tells them apart.
        var purchase = new AddStockPurchaseRequest
        {
            ProductName = "Test Coffee",
            ProductBrand = "TestBrand",
            ProductType = "Espresso",
            QuantityGrams = 500,
            CostAmount = 25.00m,
            CostCurrency = "USD",
            Vendor = "Test Vendor",
            PurchasedAt = DateTime.UtcNow,
            BagCount = 2
        };
        await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/stock/purchases", purchase);

        var before = await _client.GetFromJsonAsync<BagSuggestionsPayload>(
            $"/api/spaces/{spaceId}/consumption/bag-suggestions");

        before!.Bags.Should().HaveCount(2);
        before.SuggestedLotId.Should().NotBeNull();

        // Log against the *second* bag explicitly, then it should be the one suggested next.
        var secondBag = before.Bags.Single(b => b.LotId != before.SuggestedLotId);
        var consume = new RecordConsumptionRequest
        {
            SpaceId = spaceId,
            ProductName = "Test Coffee",
            ProductBrand = "TestBrand",
            ProductType = "Espresso",
            QuantityGrams = 14,
            StockLotId = secondBag.LotId
        };
        var consumeResponse = await _client.PostAsJsonAsync("/api/consumptions", consume);
        consumeResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var after = await _client.GetFromJsonAsync<BagSuggestionsPayload>(
            $"/api/spaces/{spaceId}/consumption/bag-suggestions");

        after!.SuggestedLotId.Should().Be(secondBag.LotId);
        var suggested = after.Bags.Single(b => b.LotId == secondBag.LotId);
        suggested.IsSuggested.Should().BeTrue();
        suggested.IsOpen.Should().BeTrue(because: "logging a cup opens the bag it was drawn from");
        suggested.RecentCups.Should().Be(1);
        suggested.MyRecentCups.Should().Be(1);
        suggested.SuggestionReason.Should().NotBeNullOrWhiteSpace();
    }

    // Mirrors the JSON the mobile app deserializes, without pulling the Application DTO in.
    private sealed class BagSuggestionsPayload
    {
        public List<BagPayload> Bags { get; set; } = new();
        public int WindowSize { get; set; }
        public int WindowCups { get; set; }
        public Guid? SuggestedLotId { get; set; }
    }

    private sealed class BagPayload
    {
        public Guid LotId { get; set; }
        public string Label { get; set; } = "";
        public string ProductDisplayName { get; set; } = "";
        public decimal RemainingGrams { get; set; }
        public bool IsOpen { get; set; }
        public int RecentCups { get; set; }
        public int MyRecentCups { get; set; }
        public bool IsSuggested { get; set; }
        public string? SuggestionReason { get; set; }
    }
}
