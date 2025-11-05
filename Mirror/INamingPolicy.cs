namespace Mirror;

public interface INamingPolicy
{
	// Turn the raw member name into a comparable "key"
	string NormalizeSource( string name );

	string NormalizeDestination( string name );
}