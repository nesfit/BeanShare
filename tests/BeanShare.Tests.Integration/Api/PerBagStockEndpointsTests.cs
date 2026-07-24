using System.Net;
using System.Net.Http.Json;
using BeanShare.Contracts.ActiveQrCodes;
using BeanShare.Contracts.CoffeeStock;
using BeanShare.Contracts.Consumption;
using BeanShare.Contracts.Spaces;
using BeanShare.Tests.Integration.Extensions;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;

namespace BeanShare.Tests.Integration.Api;

/// <summary>
/// API-head coverage for per-bag (lot) stock tracking: multi-bag purchases, open/empty bag actions,
/// lot-targeted consumption + stamped cost, the stock ledger, and QR→bag binding.
/// </summary>
public sealed class PerBagStockEndpointsTests : IClassFixture<PostgreSqlFixture>
{
    private readonly HttpClient _client;

    public PerBagStockEndpointsTests(PostgreSqlFixture fixture)
    {
        _client = fixture.CreateClient();
        _client.AsDefaultUser();
    }

    // ---- BagCount ----

    [Fact]
    public async Task AddStockPurchase_WithBagCount_CreatesSeparateBags()
    {
        var spaceId = await CreateSpaceAsync();
        await AddBagsAsync(spaceId, "Keňa Duma", "Coffeespot", 1000, 30.00m, bagCount: 3);

        var stock = await GetStockAsync(spaceId);
        stock.PurchaseCount.Should().Be(3);
        stock.TotalCurrentStockGrams.Should().Be(3000);
        stock.ProductVarietyCount.Should().Be(1);

        var bags = stock.RecentPurchases;
        bags.Should().HaveCount(3);
        bags.Should().OnlyContain(b => b.RemainingGrams == 1000 && b.Status == "Sealed" && !b.IsOpen);
        bags.Select(b => b.Label).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task AddStockPurchase_WithoutBagCount_DefaultsToOneBag()
    {
        var spaceId = await CreateSpaceAsync();
        await AddBagsAsync(spaceId, "House", "Local", 500, 12.00m); // bagCount omitted

        var stock = await GetStockAsync(spaceId);
        stock.PurchaseCount.Should().Be(1);
        stock.TotalCurrentStockGrams.Should().Be(500);
    }

    [Fact]
    public async Task AddStockPurchase_WithZeroBagCount_ReturnsBadRequest()
    {
        var spaceId = await CreateSpaceAsync();
        var request = BuildPurchase(spaceId, "X", "Y", 500, 10m) with { BagCount = 0 };

        var response = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/stock/purchases", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- OpenBag ----

    [Fact]
    public async Task OpenBag_MarksThatLotOpen()
    {
        var spaceId = await CreateSpaceAsync();
        await AddBagsAsync(spaceId, "Keňa Duma", "Coffeespot", 1000, 30.00m, bagCount: 2);
        var bags = (await GetStockAsync(spaceId)).RecentPurchases.OrderBy(b => b.Label).ToList();
        var target = bags[1];

        var open = await _client.PostAsync($"/api/spaces/{spaceId}/stock/bags/{target.Id}/open", null);
        open.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = (await GetStockAsync(spaceId)).RecentPurchases.Single(b => b.Id == target.Id);
        after.IsOpen.Should().BeTrue();
        after.Status.Should().Be("Open");
    }

    [Fact]
    public async Task OpenBag_NonexistentLot_ReturnsError()
    {
        var spaceId = await CreateSpaceAsync();
        var response = await _client.PostAsync($"/api/spaces/{spaceId}/stock/bags/{Guid.NewGuid()}/open", null);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }

    // ---- MarkBagEmpty (phantom-fill) ----

    [Fact]
    public async Task MarkBagEmpty_BillsRemainderAndEmptiesBag()
    {
        var spaceId = await CreateSpaceAsync();
        await AddBagsAsync(spaceId, "Keňa Duma", "Coffeespot", 500, 20.00m);
        var bag = (await GetStockAsync(spaceId)).RecentPurchases.Single();

        // Log a real cup so the bag has a drinker; draws from this (only) bag.
        await RecordCupAsync(spaceId, "Keňa Duma", "Coffeespot", 100);

        var empty = await _client.PostAsJsonAsync(
            $"/api/spaces/{spaceId}/stock/bags/{bag.Id}/empty",
            new MarkBagEmptyRequest { Reason = "Physically empty" });
        empty.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await empty.Content.ReadFromJsonAsync<MarkBagEmptyResponse>();
        result.Should().NotBeNull();
        result!.DistributedGrams.Should().Be(400); // 500 − 100 already consumed
        result.MemberCount.Should().BeGreaterThan(0);

        var after = (await GetStockAsync(spaceId)).RecentPurchases.Single(b => b.Id == bag.Id);
        after.Status.Should().Be("Empty");
        after.RemainingGrams.Should().Be(0);
        after.DepletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkBagEmpty_NonexistentLot_ReturnsError()
    {
        var spaceId = await CreateSpaceAsync();
        var response = await _client.PostAsJsonAsync(
            $"/api/spaces/{spaceId}/stock/bags/{Guid.NewGuid()}/empty",
            new MarkBagEmptyRequest());
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }

    // ---- Lot-targeted consumption + stamped cost ----

    [Fact]
    public async Task RecordConsumption_WithStockLotId_DrawsFromThatBagOnly()
    {
        var spaceId = await CreateSpaceAsync();
        await AddBagsAsync(spaceId, "Keňa Duma", "Coffeespot", 500, 20.00m, bagCount: 2);
        var bags = (await GetStockAsync(spaceId)).RecentPurchases.OrderBy(b => b.Label).ToList();
        var targetBag = bags[1];

        var response = await _client.PostAsJsonAsync("/api/consumptions", new RecordConsumptionRequest
        {
            SpaceId = spaceId,
            ProductName = "Keňa Duma",
            ProductBrand = "Coffeespot",
            ProductType = "Espresso",
            QuantityGrams = 50,
            StockLotId = targetBag.Id
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var after = (await GetStockAsync(spaceId)).RecentPurchases.ToDictionary(b => b.Id);
        after[targetBag.Id].RemainingGrams.Should().Be(450);
        after[bags[0].Id].RemainingGrams.Should().Be(500); // untouched — no FIFO spill
    }

    [Fact]
    public async Task RecordConsumption_StampsCostOnResponse()
    {
        var spaceId = await CreateSpaceAsync();
        await AddBagsAsync(spaceId, "Keňa Duma", "Coffeespot", 500, 20.00m);

        var response = await _client.PostAsJsonAsync("/api/consumptions", new RecordConsumptionRequest
        {
            SpaceId = spaceId,
            ProductName = "Keňa Duma",
            ProductBrand = "Coffeespot",
            ProductType = "Espresso",
            QuantityGrams = 50
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var dto = await response.Content.ReadFromJsonAsync<RecordConsumptionResponse>();
        dto.Should().NotBeNull();
        dto!.CostAmount.Should().NotBeNull();
        dto.CostAmount.Should().BeApproximately(2.00m, 0.01m); // 20.00 / 500g * 50g
        dto.CostCurrency.Should().Be("USD");
    }

    [Fact]
    public async Task RecordConsumption_TargetLotWithInsufficientGrams_RejectsWithoutSpilling()
    {
        var spaceId = await CreateSpaceAsync();
        // A small target bag plus a second bag WITH spare capacity, so we can prove the shortfall is
        // NOT spilled into the other lot (single-bag brew rule).
        await AddBagsAsync(spaceId, "Keňa Duma", "Coffeespot", 100, 5.00m);
        await AddBagsAsync(spaceId, "Keňa Duma", "Coffeespot", 500, 20.00m);
        var bags = (await GetStockAsync(spaceId)).RecentPurchases.OrderBy(b => b.QuantityGrams).ToList();
        var smallBag = bags[0];   // 100g
        var largeBag = bags[1];   // 500g

        var response = await _client.PostAsJsonAsync("/api/consumptions", new RecordConsumptionRequest
        {
            SpaceId = spaceId,
            ProductName = "Keňa Duma",
            ProductBrand = "Coffeespot",
            ProductType = "Espresso",
            QuantityGrams = 250, // more than the 100g target holds — must reject, not spill
            StockLotId = smallBag.Id
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Neither bag was touched — no partial draw, no spill into the large bag.
        var after = (await GetStockAsync(spaceId)).RecentPurchases.ToDictionary(b => b.Id);
        after[smallBag.Id].RemainingGrams.Should().Be(100);
        after[largeBag.Id].RemainingGrams.Should().Be(500);
    }

    // ---- Ledger ----

    [Fact]
    public async Task Ledger_ReturnsPurchasesAndCupsWithTotals()
    {
        var spaceId = await CreateSpaceAsync();
        await AddBagsAsync(spaceId, "Keňa Duma", "Coffeespot", 1000, 30.00m, bagCount: 2);
        await RecordCupAsync(spaceId, "Keňa Duma", "Coffeespot", 100);

        var page = await _client.GetFromJsonAsync<LedgerPageResponse>($"/api/spaces/{spaceId}/stock/ledger");
        page.Should().NotBeNull();
        page!.TotalCount.Should().Be(3); // 2 purchases + 1 cup
        page.Entries.Should().Contain(e => e.Kind == "Purchase" && e.DeltaGrams > 0);
        page.Entries.Should().Contain(e => e.Kind == "Cup" && e.DeltaGrams < 0);

        page.Totals.PurchaseCount.Should().Be(2);
        page.Totals.CupCount.Should().Be(1);
        page.Totals.PurchasedGrams.Should().Be(2000);
        page.Totals.ConsumedGrams.Should().Be(100);
        page.Totals.NetGrams.Should().Be(1900);
    }

    [Fact]
    public async Task Ledger_FilterByKind_ReturnsOnlyThatKind()
    {
        var spaceId = await CreateSpaceAsync();
        await AddBagsAsync(spaceId, "Keňa Duma", "Coffeespot", 1000, 30.00m);
        await RecordCupAsync(spaceId, "Keňa Duma", "Coffeespot", 100);

        var purchases = await _client.GetFromJsonAsync<LedgerPageResponse>(
            $"/api/spaces/{spaceId}/stock/ledger?kind=purchase");
        purchases!.Entries.Should().OnlyContain(e => e.Kind == "Purchase");

        var cups = await _client.GetFromJsonAsync<LedgerPageResponse>(
            $"/api/spaces/{spaceId}/stock/ledger?kind=cup");
        cups!.Entries.Should().OnlyContain(e => e.Kind == "Cup");
    }

    [Fact]
    public async Task Ledger_Paging_EchoesPageAndCapsEntries()
    {
        var spaceId = await CreateSpaceAsync();
        await AddBagsAsync(spaceId, "Keňa Duma", "Coffeespot", 1000, 30.00m, bagCount: 5);

        var page1 = await _client.GetFromJsonAsync<LedgerPageResponse>(
            $"/api/spaces/{spaceId}/stock/ledger?page=1&pageSize=2");
        page1!.TotalCount.Should().Be(5);
        page1.Page.Should().Be(1);
        page1.PageSize.Should().Be(2);
        page1.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task Ledger_NonexistentSpace_ReturnsError()
    {
        var response = await _client.GetAsync($"/api/spaces/{Guid.NewGuid()}/stock/ledger");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest, HttpStatusCode.Forbidden);
    }

    // ---- QR → bag binding ----

    [Fact]
    public async Task CreateQrCode_WithStockLotId_PersistsAndResolvesBinding()
    {
        var spaceId = await CreateSpaceAsync();
        await AddBagsAsync(spaceId, "Keňa Duma", "Coffeespot", 1000, 30.00m);
        var bag = (await GetStockAsync(spaceId)).RecentPurchases.Single();

        var create = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/qr-codes", new CreateActiveQrCodeRequest
        {
            SpaceId = spaceId,
            Label = "Kitchen",
            ProductName = "Keňa Duma",
            ProductBrand = "Coffeespot",
            ProductType = "Espresso",
            RecipeName = "Espresso Shot",
            DefaultGrams = 15,
            StockLotId = bag.Id
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<ActiveQrCodeResponse>();
        created!.StockLotId.Should().Be(bag.Id);

        var resolved = await _client.GetFromJsonAsync<ActiveQrCodeResponse>($"/api/qr-codes/{created.Id}/resolve");
        resolved!.StockLotId.Should().Be(bag.Id);
    }

    [Fact]
    public async Task CreateQrCode_WithoutStockLotId_HasNullBinding()
    {
        var spaceId = await CreateSpaceAsync();
        await AddBagsAsync(spaceId, "Keňa Duma", "Coffeespot", 1000, 30.00m);

        var create = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/qr-codes", new CreateActiveQrCodeRequest
        {
            SpaceId = spaceId,
            Label = "Kitchen",
            ProductName = "Keňa Duma",
            ProductBrand = "Coffeespot",
            ProductType = "Espresso",
            RecipeName = "Espresso Shot",
            DefaultGrams = 15
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<ActiveQrCodeResponse>();
        created!.StockLotId.Should().BeNull();
    }

    // ---- helpers ----

    private async Task<Guid> CreateSpaceAsync(string? name = null)
    {
        var request = new CreateSpaceRequest { Name = name ?? $"Per-Bag Test {Guid.NewGuid()}" };
        var response = await _client.PostAsJsonAsync("/api/spaces", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreateSpaceResponse>();
        return created!.SpaceId;
    }

    private static AddStockPurchaseRequest BuildPurchase(
        Guid spaceId, string name, string brand, decimal grams, decimal cost, int bagCount = 1) =>
        new()
        {
            BodySpaceId = spaceId,
            ProductName = name,
            ProductBrand = brand,
            ProductType = "Espresso",
            QuantityGrams = grams,
            CostAmount = cost,
            CostCurrency = "USD",
            Vendor = "Test Vendor",
            PurchasedAt = DateTime.UtcNow.AddDays(-1),
            BagCount = bagCount
        };

    private async Task AddBagsAsync(Guid spaceId, string name, string brand, decimal grams, decimal cost, int bagCount = 1)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/spaces/{spaceId}/stock/purchases", BuildPurchase(spaceId, name, brand, grams, cost, bagCount));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<SpaceStockResponse> GetStockAsync(Guid spaceId)
    {
        var response = await _client.GetAsync($"/api/spaces/{spaceId}/stock");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stock = await response.Content.ReadFromJsonAsync<SpaceStockResponse>();
        return stock!;
    }

    private async Task RecordCupAsync(Guid spaceId, string name, string brand, decimal grams)
    {
        var response = await _client.PostAsJsonAsync("/api/consumptions", new RecordConsumptionRequest
        {
            SpaceId = spaceId,
            ProductName = name,
            ProductBrand = brand,
            ProductType = "Espresso",
            QuantityGrams = grams
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }
}
