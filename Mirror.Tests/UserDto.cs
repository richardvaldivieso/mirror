namespace Mirror.Tests;

public sealed class UserDto
{
	public string FirstName { get; set; }
	public string LastName { get; set; }
	public int Age { get; set; }
	public AddressDto Home { get; set; }
	public List<string> Tags { get; set; }
	public Role Role { get; set; }
}
