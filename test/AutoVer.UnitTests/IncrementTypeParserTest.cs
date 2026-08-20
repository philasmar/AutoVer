using AutoVer.Exceptions;
using AutoVer.Models;

namespace AutoVer.UnitTests;

public class IncrementTypeParserTest
{
    [Test]
    [Arguments("None", IncrementType.None)]
    [Arguments("Patch", IncrementType.Patch)]
    [Arguments("Minor", IncrementType.Minor)]
    [Arguments("Major", IncrementType.Major)]
    [Arguments("patch", IncrementType.Patch)]
    [Arguments("PATCH", IncrementType.Patch)]
    [Arguments("mInOr", IncrementType.Minor)]
    public async Task Parse_ValidValue_IsCaseInsensitive(string value, IncrementType expected)
    {
        await Assert.That(IncrementTypeParser.Parse(value)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("0")]
    [Arguments("1")]
    [Arguments("99")]
    [Arguments("-1")]
    public async Task Parse_NumericValue_ThrowsInvalidArgumentException(string value)
    {
        await Assert.That(() => IncrementTypeParser.Parse(value)).Throws<InvalidArgumentException>();
    }

    [Test]
    [Arguments("NotARealIncrementType")]
    [Arguments("")]
    [Arguments(null)]
    public async Task Parse_InvalidValue_ThrowsInvalidArgumentException(string? value)
    {
        await Assert.That(() => IncrementTypeParser.Parse(value)).Throws<InvalidArgumentException>();
    }

    // IncrementType isn't [Flags], but Enum.TryParse still accepts comma-separated names
    // combined via bitwise OR (e.g. Patch(1)|Minor(2) == 3 == Major). Parse must reject this
    // rather than silently resolving to a different, valid-looking value.
    [Test]
    [Arguments("Patch,Minor")]
    [Arguments("Minor,Patch")]
    [Arguments("patch,minor")]
    [Arguments("Patch, Minor")]
    public async Task Parse_CommaSeparatedNames_ThrowsInvalidArgumentException(string value)
    {
        await Assert.That(() => IncrementTypeParser.Parse(value)).Throws<InvalidArgumentException>();
    }
}
