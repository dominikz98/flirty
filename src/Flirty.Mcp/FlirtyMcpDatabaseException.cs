namespace Flirty.Mcp;

/// <summary>
/// A database-level failure of one of the <c>flirty_db_*</c> tools: the target is unreachable, or its
/// migrations assembly is missing. Mapped to <c>500 Database error</c> by
/// <see cref="FlirtyMcpExceptionFilter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately derived from <see cref="Exception"/> and <b>not</b> from
/// <see cref="InvalidOperationException"/>: the latter would make CS0160 force its catch branch above the
/// <c>409 Conflict</c> one and split the six branches that read verbatim like the HTTP filter. It is also
/// the honest status – nothing about the request is in conflict, the database simply did not answer.
/// </para>
/// <para>
/// Only two of the four database tools raise it. <c>flirty_db_test_connection</c> never does, because
/// "not reachable" is the <i>answer</i> to what it was asked; the other two cannot answer at all, so
/// their failure belongs on the error channel where a model will not read past it.
/// </para>
/// </remarks>
internal sealed class FlirtyMcpDatabaseException : Exception
{
    /// <summary>The assembly-name prefix of the three provider-separated migration assemblies.</summary>
    private const string MigrationsAssemblyPrefix = "Flirty.Migrations.";

    private FlirtyMcpDatabaseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Wraps a provider or EF Core failure with a message a caller can act on.</summary>
    /// <param name="exception">The original failure.</param>
    /// <returns>The wrapped failure.</returns>
    internal static FlirtyMcpDatabaseException For(Exception exception) =>
        new(Describe(exception), exception);

    /// <summary>
    /// Turns a database failure into a legible message, translating the one failure mode whose own
    /// message explains nothing: a missing <c>Flirty.Migrations.*</c> assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Flirty.Mcp</c> deliberately references none of the three migration projects – a
    /// <c>ProjectReference</c> from a packable to a non-packable project yields neither a
    /// <c>&lt;dependency&gt;</c> nor a bundled DLL, so the assembly would simply vanish from the package.
    /// NuGet consumers get all three inside the <c>Flirty</c> package; a host building from source has to
    /// reference them itself, exactly as <c>Flirty.Designer</c> does. Without this translation that host
    /// gets a bare <c>FileNotFoundException</c> and no hint at which of the three is meant.
    /// </para>
    /// <para>
    /// The assembly name is read <b>out of the exception</b> rather than derived from the target's
    /// provider on purpose: the provider-to-assembly mapping lives in exactly one place
    /// (<c>UseFlirtyProvider</c>), and a copy here would be a second truth that could drift.
    /// </para>
    /// </remarks>
    /// <param name="exception">The failure to describe.</param>
    /// <returns>The translated message, or the original one when nothing better is known.</returns>
    internal static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // EF Core does not wrap the Assembly.Load failure, but a provider further down the chain might,
        // so the whole chain is walked rather than only the outermost exception.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (MissingMigrationsAssembly(current) is not { } assembly)
            {
                continue;
            }

            return $"The migrations assembly '{assembly}' could not be loaded. Reference it from the host "
                + "application: the Flirty NuGet package bundles all three, a host building from source "
                + $"adds a project reference to '{assembly}' as Flirty.Designer does.";
        }

        return exception.Message;
    }

    /// <summary>
    /// The simple name of the Flirty migrations assembly the exception failed to load, if that is what it
    /// is.
    /// </summary>
    private static string? MissingMigrationsAssembly(Exception exception)
    {
        var fileName = exception switch
        {
            FileNotFoundException notFound => notFound.FileName,
            FileLoadException loadFailure => loadFailure.FileName,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        // The file name is the assembly display name ("Flirty.Migrations.Sqlite, Culture=neutral, …").
        // Split rather than AssemblyName, which throws on a malformed name and would need a second guard.
        var simpleName = fileName.Split(',')[0].Trim();

        return simpleName.StartsWith(MigrationsAssemblyPrefix, StringComparison.Ordinal)
            ? simpleName
            : null;
    }
}
