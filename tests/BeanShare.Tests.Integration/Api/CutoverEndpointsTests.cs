using System.Net;
using System.Net.Http.Json;
using BeanShare.Application.Features.Balances.Queries.GetPhantomCandidates;
using BeanShare.Contracts.CoffeeStock;
using BeanShare.Contracts.Spaces;
using BeanShare.Domain.ValueObjects;
using BeanShare.Infrastructure.Persistence;
using BeanShare.Tests.Integration.Extensions;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Tests.Integration.Api;

// The paid-in-Excel and void-phantom endpoints.
public sealed class CutoverEndpointsTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly HttpClient _client;

    public CutoverEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
    }

    private static DateTime Today => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    [Fact]
    public async Task PaidInExcel_FlagsAnOldBag_RefusesANewOne_AndANonAdmin()
    {
        var (spaceId, invite) = await CreateSpaceAsync("Cutover Flags");
        var oldBag = await AddBagAsync(spaceId, "Old", Today.AddDays(-5));
        var newBag = await AddBagAsync(spaceId, "New", DateTime.UtcNow);
        (await _client.PutAsJsonAsync($"/api/spaces/{spaceId}/ledger-settings",
            new { TreasurerUserId = FixtureUsers.DefaultUserId, LedgerStartDate = Today })).EnsureSuccessStatusCode();

        var flagged = await _client.PutAsJsonAsync($"/api/spaces/{spaceId}/stock/purchases/{oldBag}/paid-in-excel", new { PaidInExcel = true });
        flagged.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var tooNew = await _client.PutAsJsonAsync($"/api/spaces/{spaceId}/stock/purchases/{newBag}/paid-in-excel", new { PaidInExcel = true });
        tooNew.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var member = await JoinAsync(invite);
        _client.WithTestUser(member);
        var forbidden = await _client.PutAsJsonAsync($"/api/spaces/{spaceId}/stock/purchases/{oldBag}/paid-in-excel", new { PaidInExcel = false });
        _client.AsDefaultUser();
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PaidInExcel_AfterTheFirstLedgerSettlement_IsLocked()
    {
        var (spaceId, _) = await CreateSpaceAsync("Cutover Locked");
        var oldBag = await AddBagAsync(spaceId, "Old", Today.AddDays(-5));
        (await _client.PutAsJsonAsync($"/api/spaces/{spaceId}/ledger-settings",
            new { TreasurerUserId = FixtureUsers.DefaultUserId, LedgerStartDate = Today.AddDays(-2) })).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/billing-periods",
            new { SpaceId = spaceId, Name = "First", StartDate = Today.AddDays(-2), EndDate = Today.AddDays(-1) })).EnsureSuccessStatusCode();
        var periodId = (await _client.GetFromJsonAsync<List<PeriodRow>>($"/api/spaces/{spaceId}/billing-periods"))!.Single().Id;
        (await _client.PutAsJsonAsync($"/api/billing-periods/{periodId}/open", new { })).EnsureSuccessStatusCode();
        (await _client.PutAsJsonAsync($"/api/billing-periods/{periodId}/close", new { })).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/billing-periods/{periodId}/settlement", new { })).EnsureSuccessStatusCode();

        var locked = await _client.PutAsJsonAsync($"/api/spaces/{spaceId}/stock/purchases/{oldBag}/paid-in-excel", new { PaidInExcel = true });

        locked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await locked.Content.ReadAsStringAsync()).Should().Contain("LEDGER_START_LOCKED");
    }

    [Fact]
    public async Task VoidPhantom_VoidsWriteOffShares_RefusesANormalCup_AndANonAdmin()
    {
        var (spaceId, invite) = await CreateSpaceAsync("Cutover Void");
        var bag = await AddBagAsync(spaceId, "Bag", Today.AddDays(-5));
        var other = await AddBagAsync(spaceId, "Other", Today.AddDays(-5), product: "Gamma");
        (await _client.PutAsJsonAsync($"/api/spaces/{spaceId}/ledger-settings",
            new { TreasurerUserId = FixtureUsers.DefaultUserId, LedgerStartDate = Today })).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/stock/bags/{bag}/empty", new MarkBagEmptyRequest())).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync("/api/consumptions", new
        {
            SpaceId = spaceId, ProductName = "Gamma", ProductBrand = "Roaster", ProductType = "Filter", QuantityGrams = 10m, StockLotId = other
        })).EnsureSuccessStatusCode();

        var candidates = await _client.GetFromJsonAsync<PhantomCandidatesDto>($"/api/spaces/{spaceId}/balances/cutover/phantom-candidates");
        var shares = candidates!.Events.SelectMany(e => e.Rows).Select(r => r.ConsumptionId).ToList();
        shares.Should().NotBeEmpty();
        var normalCup = await NormalCupAsync(spaceId);

        var notVoidable = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/consumptions/void-phantom",
            new { ConsumptionIds = new[] { normalCup }, Reason = "phantom" });
        notVoidable.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await notVoidable.Content.ReadAsStringAsync()).Should().Contain("NOT_VOIDABLE");

        var member = await JoinAsync(invite);
        _client.WithTestUser(member);
        var forbidden = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/consumptions/void-phantom",
            new { ConsumptionIds = shares, Reason = "phantom" });
        _client.AsDefaultUser();
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var voided = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/consumptions/void-phantom",
            new { ConsumptionIds = shares, Reason = "phantom" });
        voided.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<Guid> NormalCupAsync(Guid spaceId)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BeanShareDbContext>();
        var id = new SpaceId(spaceId);
        var cups = await db.Consumptions.AsNoTracking().Where(c => c.SpaceId == id).ToListAsync();
        return cups.Single(c => !c.IsWriteOff && c.StockLotId != null).Id.Value;
    }

    private async Task<Guid> AddBagAsync(Guid spaceId, string label, DateTime purchasedAt, string product = "Beta")
    {
        var response = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/stock/purchases", new AddStockPurchaseRequest
        {
            BodySpaceId = spaceId,
            ProductName = product,
            ProductBrand = "Roaster",
            ProductType = "Filter",
            QuantityGrams = 1000m,
            CostAmount = 600m,
            CostCurrency = "USD",
            Vendor = label,
            PurchasedAt = purchasedAt
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var stock = await _client.GetFromJsonAsync<SpaceStockResponse>($"/api/spaces/{spaceId}/stock?pageSize=100");
        return stock!.RecentPurchases.Single(p => p.Vendor == label).Id;
    }

    private async Task<(Guid SpaceId, string InviteCode)> CreateSpaceAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/spaces", new CreateSpaceRequest { Name = name });
        var created = await response.Content.ReadFromJsonAsync<CreateSpaceResponse>();
        return (created!.SpaceId, created.InviteCode);
    }

    private async Task<Guid> JoinAsync(string inviteCode)
    {
        var userId = Guid.NewGuid();
        await _fixture.EnsureUserAsync(userId, $"{userId:N}@test.com");
        _client.WithTestUser(userId, $"{userId:N}@test.com");
        var response = await _client.PostAsJsonAsync("/api/spaces/join", new JoinSpaceRequest { InviteCode = inviteCode });
        _client.AsDefaultUser();
        response.EnsureSuccessStatusCode();
        return userId;
    }

    private sealed record PeriodRow(Guid Id);
}
