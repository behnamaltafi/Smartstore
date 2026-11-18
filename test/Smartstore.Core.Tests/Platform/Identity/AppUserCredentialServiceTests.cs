using System.Threading.Tasks;
using NUnit.Framework;
using Smartstore.Core.Identity;

namespace Smartstore.Core.Tests.Platform.Identity;

[TestFixture]
public sealed class AppUserCredentialServiceTests
{
    // Invalid input must fail before any persistence or HTTP dependencies
    // are consulted. Successful flows are covered by migrated DB integration tests.
    private static AppUserCredentialService Create() => new(null);

    [TestCase("person", "person@example.test", "weak")]
    [TestCase("", "person@example.test", "VeryLongPassword123!")]
    [TestCase("person", "invalid", "VeryLongPassword123!")]
    public async Task RegistrationRejectsInvalidCredentialInputs(
        string username, string email, string password)
    {
        Assert.That(await Create().RegisterAsync(username, email, password), Is.Null);
    }

    [Test]
    public async Task PasswordLoginMustHaveIdentifierAndSecret()
    {
        Assert.That(await Create().AuthenticatePasswordAsync(null, "password"), Is.Null);
        Assert.That(await Create().AuthenticatePasswordAsync("person", null), Is.Null);
    }

    [Test]
    public async Task PasswordResetWithNoTokenMustFail()
    {
        Assert.That(await Create().CompletePasswordResetAsync(
            "person@example.test", "", "VeryLongPassword123!"), Is.False);
    }

    [Test]
    public async Task MalformedOtpCannotLookUpAnyAccount()
    {
        Assert.That(await Create().VerifyOtpAsync("+44123456789", "1"), Is.Null);
    }

    [Test]
    public async Task MissingProviderPreventsExternalLogin()
    {
        Assert.That(await Create().AuthenticateExternalAsync("", "subject", "test@example.test"), Is.Null);
    }
}
