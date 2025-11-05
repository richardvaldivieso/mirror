using System.Text.RegularExpressions;

namespace Mirror;

public sealed class UniversalToPascalPolicy : INamingPolicy
{
	// Splits by underscores, hyphens, spaces, and camel humps / digit boundaries,
	// then TitleCases each token and concatenates -> PascalCase.
	public string NormalizeSource( string name ) => ToPascal( name );

	public string NormalizeDestination( string name ) => ToPascal( name );

	private static readonly Regex Splitter = new Regex(
		@"[_\-\s]+
        |(?<=[a-z])(?=[A-Z])
        |(?<=[A-Za-z])(?=[0-9])
        |(?<=[0-9])(?=[A-Za-z])
        |(?<=[A-Z])(?=[A-Z][a-z])",
	RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace );

	private static string ToPascal( string? s )
	{
		if ( string.IsNullOrEmpty( s ) ) return string.Empty;

		var tokens = Splitter.Split( s ).Where( t => t.Length > 0 ).ToList();
		if ( tokens.Count == 0 ) return string.Empty;

		for ( int i = 0; i < tokens.Count; i++ )
		{
			var t = tokens[i].ToLowerInvariant(); // normalize EVERYTHING first
			tokens[i] = char.ToUpperInvariant( t[0] ) + ( t.Length > 1 ? t.Substring( 1 ) : string.Empty );
		}

		return string.Concat( tokens );
	}
}