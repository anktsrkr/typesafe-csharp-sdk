namespace TypeSafe.AI;

/// <summary>One immutable snapshot shared by the DI client and its handler pipeline.</summary>
internal sealed record TypeSafeClientSettings(
    string ApiKey, string BaseUrl, string DefaultModel, int MaxRetries,
    TimeSpan BackoffInitial, TimeSpan BackoffMax, bool UseJitter, bool RespectRetryAfter,
    TimeSpan? PerAttemptTimeout, TimeSpan? TotalTimeoutBudget)
{
    public static TypeSafeClientSettings Create(TypeSafeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var retry = options.Retry;
        return new(options.ApiKey!, options.BaseUrl, options.DefaultModel, retry.MaxRetries,
            retry.BackoffInitial, retry.BackoffMax, retry.UseJitter, retry.RespectRetryAfter,
            retry.PerAttemptTimeout, retry.TotalTimeoutBudget);
    }

    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"{nameof(ApiKey)} = [REDACTED], ");
        builder.Append($"{nameof(BaseUrl)} = {BaseUrl}, ");
        builder.Append($"{nameof(DefaultModel)} = {DefaultModel}, ");
        builder.Append($"{nameof(MaxRetries)} = {MaxRetries}, ");
        builder.Append($"{nameof(BackoffInitial)} = {BackoffInitial}, ");
        builder.Append($"{nameof(BackoffMax)} = {BackoffMax}, ");
        builder.Append($"{nameof(UseJitter)} = {UseJitter}, ");
        builder.Append($"{nameof(RespectRetryAfter)} = {RespectRetryAfter}, ");
        builder.Append($"{nameof(PerAttemptTimeout)} = {PerAttemptTimeout}, ");
        builder.Append($"{nameof(TotalTimeoutBudget)} = {TotalTimeoutBudget}");
        return true;
    }
}
