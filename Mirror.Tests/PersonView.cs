namespace Mirror.Tests;

// ctor-only, no parameterless ctor
public sealed class PersonView
{
	public string Name { get; }
	public int Age { get; set; }

	public PersonView( string name ) => Name = name;
}