using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BeanShare.Application.Features.CoffeeStock.Dtos;
using BeanShare.Application.Features.Consumption.Dtos;
using BeanShare.Application.Features.Consumption.Queries;
using BeanShare.Application.Features.Consumption.Queries.GetBagSuggestions;
using BeanShare.Application.Features.Spaces.Dtos;
using BeanShare.Contracts.CoffeeStock;
using BeanShare.Contracts.Consumption;
using BeanShare.Contracts.Spaces;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;

namespace BeanShare.Tests.Integration.Api;

/// <summary>
/// The composite dashboard is hand-projected into anonymous types, so nothing but a test keeps it
/// in sync with the DTOs mobile binds it to. A dropped <c>required</c> member does not degrade
/// gracefully — System.Text.Json throws and the client silently falls back to N round-trips.
/// </summary>
public sealed class SpaceDashboardEndpointTests : IClassFixture<PostgreSqlFixture>
{
    private readonly HttpClient _client;

    // What HttpClient.GetFromJsonAsync uses on the mobile side.
    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    public SpaceDashboardEndpointTests(PostgreSqlFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task GetDashboard_ShouldDeserializeIntoTheDtosMobileBindsItTo()
    {
        var spaceId = await CreateSpaceWithActivityAsync();

        var response = await _client.GetAsync($"/api/spaces/{spaceId}/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<DashboardPayload>(WebDefaults);

        payload.Should().NotBeNull();
        payload!.Space.Should().NotBeNull();
        payload.Space!.Members.Should().NotBeEmpty();

        payload.Stock.Should().NotBeNull();
        payload.Stock!.StockLevels.Should().ContainSingle();
        payload.Stock.StockLevels[0].ProductName.Should().Be("Dashboard Coffee");

        payload.Consumption.Should().NotBeNull();
        payload.Consumption!.Entries.Should().ContainSingle();

        payload.Presets.Should().NotBeNull();

        payload.Bags.Should().NotBeNull();
        payload.Bags!.Bags.Should().ContainSingle();
        payload.Bags.SuggestedLotId.Should().NotBeNull();
        payload.Bags.Bags[0].IsSuggested.Should().BeTrue();
        payload.Bags.Bags[0].RecentCups.Should().Be(1, because: "one cup was logged from this bag");
    }

    [Fact]
    public async Task GetDashboard_StockLevels_ShouldCarryEveryRequiredMemberOfStockLevelDto()
    {
        var spaceId = await CreateSpaceWithActivityAsync();

        var response = await _client.GetAsync($"/api/spaces/{spaceId}/dashboard");
        var json = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);
        var stockLevel = document.RootElement
            .GetProperty("Stock")
            .GetProperty("StockLevels")[0];

        // Deserializing the element on its own is the exact operation that used to throw.
        var act = () => stockLevel.Deserialize<StockLevelDto>(WebDefaults);
        act.Should().NotThrow();

        var dto = stockLevel.Deserialize<StockLevelDto>(WebDefaults)!;
        dto.IsCurrentlyUsed.Should().BeTrue(because: "the cup that was logged opened the bag");
        dto.UpdatedAt.Should().NotBe(default);
    }

    private async Task<Guid> CreateSpaceWithActivityAsync()
    {
        var createResponse = await _client.PostAsJsonAsync(
            "/api/spaces", new CreateSpaceRequest { Name = "Dashboard Payload Test" });
        var spaceId = (await createResponse.Content.ReadFromJsonAsync<CreateSpaceResponse>())!.SpaceId;

        await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/stock/purchases", new AddStockPurchaseRequest
        {
            ProductName = "Dashboard Coffee",
            ProductBrand = "TestBrand",
            ProductType = "Espresso",
            QuantityGrams = 500,
            CostAmount = 25.00m,
            CostCurrency = "USD",
            Vendor = "Test Vendor",
            PurchasedAt = DateTime.UtcNow
        });

        await _client.PostAsJsonAsync("/api/consumptions", new RecordConsumptionRequest
        {
            SpaceId = spaceId,
            ProductName = "Dashboard Coffee",
            ProductBrand = "TestBrand",
            ProductType = "Espresso",
            QuantityGrams = 14
        });

        return spaceId;
    }

    // Mirrors BeanShare.Maui's SpaceDashboardData — the MAUI head is outside this solution filter,
    // so the shape is restated here rather than referenced.
    private sealed class DashboardPayload
    {
        public DashboardSpace? Space { get; set; }
        public DashboardStock? Stock { get; set; }
        public GetRecentConsumptionsResult? Consumption { get; set; }
        public DashboardPresets? Presets { get; set; }
        public BagSuggestionsResult? Bags { get; set; }
    }

    private sealed class DashboardSpace
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string InviteCode { get; set; } = "";
        public string CurrencyCode { get; set; } = "";
        public bool IsActive { get; set; }
        public int MemberCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<MembershipDto> Members { get; set; } = new();
    }

    private sealed class DashboardStock
    {
        public decimal TotalCurrentStockGrams { get; set; }
        public decimal TotalInvestmentAmount { get; set; }
        public string TotalInvestmentCurrency { get; set; } = "";
        public List<StockLevelDto> StockLevels { get; set; } = new();
    }

    private sealed class DashboardPresets
    {
        public List<QuickPresetPayload>? Presets { get; set; }
    }

    private sealed class QuickPresetPayload
    {
        public Guid? GlobalPresetId { get; set; }
        public Guid? SpacePresetId { get; set; }
        public string Name { get; set; } = "";
        public decimal DefaultGrams { get; set; }
        public bool IsFavorite { get; set; }
    }
}
