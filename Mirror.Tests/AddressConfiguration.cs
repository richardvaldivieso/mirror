namespace Mirror.Tests;

public sealed class AddressConfiguration : IConfiguration<AddressDto, Address>
{
	public bool Reflect => true;

	public void Configure( MirrorMap<AddressDto, Address> map )
	{
		// convention only
	}
}