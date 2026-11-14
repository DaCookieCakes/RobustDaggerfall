using YamlDotNet.Core;

namespace Engine.Shared.Serialization;

/// <summary>
///		Thrown when YAML can't be read into the requested type.
/// </summary>
public sealed class YamlReadException : Exception
{
	/// <summary>
	///		Where in the YAML the problem is, if known.
	/// </summary>
	public Mark? Location { get; }

	public YamlReadException(string message, Mark? location = null, Exception? inner = null)
		: base(location is { Line: > 0 } mark ? $"({mark.Line}:{mark.Column}) {message}" : message, inner)
	{
		Location = location;
	}
}
