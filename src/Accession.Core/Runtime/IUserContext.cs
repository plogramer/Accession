namespace Accession.Core.Runtime;

/// <summary>Who is running the application, for the audit trail and the inventory lock.</summary>
public interface IUserContext
{
    /// <summary>Windows account as <c>DOMAIN\user</c>.</summary>
    string UserName { get; }

    string MachineName { get; }
}

public sealed class EnvironmentUserContext : IUserContext
{
    public string UserName { get; } = $@"{Environment.UserDomainName}\{Environment.UserName}";

    public string MachineName { get; } = Environment.MachineName;
}
