using Engine.Shared.Serialization;
using YamlDotNet.RepresentationModel;

namespace Engine.Tests;

public class SerializationTests
{
	private readonly SerializationManager _serialization = new();

	/// <summary>
	///		Parses YAML text and returns its top node.
	/// </summary>
	private static YamlNode Parse(string yaml)
	{
		var stream = new YamlStream();
		stream.Load(new StringReader(yaml));
		return stream.Documents[0].RootNode;
	}

	private enum Faction
	{
		Neutral,
		Hostile,
	}

	[Fact]
	public void ReadsPrimitives()
	{
		Assert.Equal(30, _serialization.Read<int>(Parse("30")));
		Assert.Equal(1.5f, _serialization.Read<float>(Parse("1.5")));
		Assert.True(_serialization.Read<bool>(Parse("true")));
		Assert.Equal("goblin", _serialization.Read<string>(Parse("goblin")));
		Assert.Equal(Faction.Hostile, _serialization.Read<Faction>(Parse("Hostile")));
	}

	[Fact]
	public void UnquotedNullIsNull()
	{
		Assert.Null(_serialization.Read<string?>(Parse("null")));
		Assert.Null(_serialization.Read<int?>(Parse("~")));
	}

	[Fact]
	public void QuotedNullIsAString()
	{
		Assert.Equal("null", _serialization.Read<string>(Parse("\"null\"")));
	}

	[Fact]
	public void NullableReadsLikeItsValueType()
	{
		Assert.Equal(5, _serialization.Read<int?>(Parse("5")));
	}

	[Theory]
	[InlineData("lots", "'lots' is not a valid Int32")]
	[InlineData("null", "Expected Int32, got null")]
	[InlineData("[1, 2]", "Expected a value for Int32, got Sequence")]
	[InlineData("99999999999", "'99999999999' is not a valid Int32")]
	public void BadIntsGiveUsefulErrors(string yaml, string expected)
	{
		var error = Assert.Throws<YamlReadException>(() => _serialization.Read<int>(Parse(yaml)));
		Assert.Contains(expected, error.Message);
	}

	[Fact]
	public void EnumsAreCaseSensitive()
	{
		Assert.Throws<YamlReadException>(() => _serialization.Read<Faction>(Parse("hostile")));
	}

	[Fact]
	public void UnsupportedTypesSayWhat()
	{
		var error = Assert.Throws<YamlReadException>(() => _serialization.Read<Uri>(Parse("x")));
		Assert.Contains("Don't know how to read Uri", error.Message);
	}

	[Fact]
	public void ErrorsIncludeLineAndColumn()
	{
		var mapping = (YamlMappingNode) Parse("name: goblin\ncurrent: lots");
		var value = mapping.Children[new YamlScalarNode("current")];

		var error = Assert.Throws<YamlReadException>(() => _serialization.Read<int>(value));
		Assert.StartsWith("(2:10)", error.Message);
	}
}
