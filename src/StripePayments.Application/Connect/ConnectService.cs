using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StripePayments.Application.Abstractions;
using StripePayments.Application.Common;
using StripePayments.Domain.Connect;

namespace StripePayments.Application.Connect;

public sealed class ConnectOptions
{
    public const string Section = "Connect";

    /// <summary>Platform fee taken from destination charges, in percent of the charged amount.</summary>
    public decimal PlatformFeePercent { get; init; } = 10m;

    public long CalculateFee(long amount) => (long)Math.Round(amount * PlatformFeePercent / 100m, MidpointRounding.AwayFromZero);
}

public sealed record CreateConnectedAccountRequest(string Email);

public sealed class CreateConnectedAccountRequestValidator : AbstractValidator<CreateConnectedAccountRequest>
{
    public CreateConnectedAccountRequestValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress();
}

public sealed record ConnectedAccountDto(Guid Id, string StripeAccountId, string Email, bool ChargesEnabled, bool PayoutsEnabled, bool DetailsSubmitted)
{
    public static ConnectedAccountDto From(ConnectedAccount a) =>
        new(a.Id, a.StripeAccountId, a.Email, a.ChargesEnabled, a.PayoutsEnabled, a.DetailsSubmitted);
}

public sealed record OnboardingLinkResponse(ConnectedAccountDto Account, string OnboardingUrl);

public sealed record PayoutStatusResponse(ConnectedAccountDto Account, IReadOnlyList<BalanceAmount> Available,
    IReadOnlyList<BalanceAmount> Pending, IReadOnlyList<PayoutInfo> RecentPayouts);

public sealed class ConnectService(IAppDbContext db, IStripeGateway stripe, IOptions<AppOptions> app, TimeProvider clock)
{
    public async Task<OnboardingLinkResponse> CreateAccountAsync(CreateConnectedAccountRequest request, CancellationToken ct)
    {
        if (!stripe.IsConfigured) throw new StripeNotConfiguredException();

        var stripeAccountId = await stripe.CreateExpressAccountAsync(request.Email, ct);
        var account = ConnectedAccount.Create(stripeAccountId, request.Email, clock.GetUtcNow());
        db.ConnectedAccounts.Add(account);
        await db.SaveChangesAsync(ct);

        return new OnboardingLinkResponse(ConnectedAccountDto.From(account), await OnboardingUrl(account, ct));
    }

    /// <summary>Account links expire after a few minutes, so a fresh one is created on every request.</summary>
    public async Task<OnboardingLinkResponse> CreateOnboardingLinkAsync(Guid id, CancellationToken ct)
    {
        var account = await Get(id, ct);
        return new OnboardingLinkResponse(ConnectedAccountDto.From(account), await OnboardingUrl(account, ct));
    }

    /// <summary>Live balance and payouts from Stripe (read-only, not stored).</summary>
    public async Task<PayoutStatusResponse> GetPayoutStatusAsync(Guid id, CancellationToken ct)
    {
        var account = await Get(id, ct);
        var balance = await stripe.GetBalanceAsync(account.StripeAccountId, ct);
        var payouts = await stripe.ListPayoutsAsync(account.StripeAccountId, 10, ct);
        return new PayoutStatusResponse(ConnectedAccountDto.From(account), balance.Available, balance.Pending, payouts);
    }

    public async Task<IReadOnlyList<ConnectedAccountDto>> ListAsync(CancellationToken ct) =>
        (await db.ConnectedAccounts.AsNoTracking().OrderBy(a => a.CreatedAt).ToListAsync(ct))
        .Select(ConnectedAccountDto.From).ToList();

    private async Task<ConnectedAccount> Get(Guid id, CancellationToken ct) =>
        await db.ConnectedAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new NotFoundException("Connected account", id);

    private Task<string> OnboardingUrl(ConnectedAccount account, CancellationToken ct)
    {
        var page = $"{app.Value.PublicBaseUrl.TrimEnd('/')}/connect.html?account={account.Id}";
        return stripe.CreateAccountOnboardingLinkAsync(account.StripeAccountId, refreshUrl: page + "&refresh=1", returnUrl: page, ct);
    }
}
