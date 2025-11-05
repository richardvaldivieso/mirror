namespace Mirror.Tests;

public sealed class User
{
	public string FirstName { get; set; }
	public string LastName { get; set; }
	public int? Age { get; set; }                 // note: nullable target
	public Address Home { get; set; }             // nested
	public List<string> Tags { get; set; }        // collection
	public string SecretNote { get; set; }        // will be ignored
	public string FullName { get; set; }          // custom resolver
	public string Role { get; set; }              // enum -> string
}
