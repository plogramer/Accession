using Accession.Core.Runtime;

namespace Accession.Tests.Runtime;

public class EnvironmentUserContextTests
{
    [Fact]
    public void User_name_is_domain_backslash_user()
    {
        var context = new EnvironmentUserContext();

        Assert.Equal($@"{Environment.UserDomainName}\{Environment.UserName}", context.UserName);
        Assert.Equal(Environment.MachineName, context.MachineName);
    }
}
