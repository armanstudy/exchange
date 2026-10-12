using LenjExchange.Application.Security;
using LenjExchange.Domain.Common;

namespace LenjExchange.Tests.Domain;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_verifies_and_uses_random_salt()
    {
        var h = new PasswordHasher();
        var a = h.Hash("S3cret-رمز");
        var b = h.Hash("S3cret-رمز");
        Assert.NotEqual(a, b);
        Assert.StartsWith("PBKDF2-SHA256$600000$", a);
        Assert.True(h.Verify("S3cret-رمز", a));
        Assert.False(h.Verify("s3cret-رمز", a));
        Assert.False(h.Verify("", a));
        Assert.False(h.Verify("x", "garbage"));
        Assert.False(h.Verify("x", "PBKDF2-SHA256$1000$!!$!!"));
    }

    [Fact]
    public void Rehash_needed_when_iterations_change()
    {
        var weak = new PasswordHasher(10_000).Hash("Password1");
        var strong = new PasswordHasher();
        Assert.True(strong.Verify("Password1", weak));
        Assert.True(strong.NeedsRehash(weak));
        Assert.False(strong.NeedsRehash(strong.Hash("Password1")));
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("onlyletters")]
    [InlineData("1234567890")]
    [InlineData("admin1234")]
    public void Weak_passwords_rejected(string pw) =>
        Assert.Throws<DomainException>(() => UserService.ValidatePassword(pw, "admin1234"));
}
