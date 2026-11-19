using NUnit.Framework;
using Smartstore.MultiSms.Services;

namespace Smartstore.Core.Tests.Platform.Messaging;

[TestFixture]
public sealed class SmsPhoneNormalizerTests
{
    [TestCase("09123456789", "+989123456789")]
    [TestCase("989123456789", "+989123456789")]
    [TestCase("+989123456789", "+989123456789")]
    [TestCase(" 09123456789 ", "+989123456789")]
    [TestCase("۰۹۱۲۳۴۵۶۷۸۹", "+989123456789")]
    [TestCase("٠٩١٢٣٤٥٦٧٨٩", "+989123456789")]
    public void Iranian_mobile_numbers_have_a_single_canonical_identity(string input, string expected)
        => Assert.That(SmsPhoneNormalizer.Normalize(input), Is.EqualTo(expected));

    [TestCase("09123")]
    [TestCase("+44123456789")]
    [TestCase("0912-345-6789")]
    [TestCase("0912345678x")]
    [TestCase(null)]
    public void Invalid_or_unsupported_numbers_are_not_enrolled(string input)
        => Assert.That(SmsPhoneNormalizer.Normalize(input), Is.Null);

    [Test]
    public void Canonical_iranian_number_is_sent_in_domestic_provider_format()
        => Assert.That(SmsPhoneNormalizer.ToProviderPhone("+989123456789"),
            Is.EqualTo("09123456789"));
}
