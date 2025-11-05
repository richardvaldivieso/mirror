namespace Mirror.Tests;

public sealed class UserConfiguration : IConfiguration<UserDto, User>
{
	public bool Reflect => true; // also register User -> UserDto

	public void Configure( MirrorMap<UserDto, User> map )
	{
		map.Ignore( d => d.SecretNote )
		   .ForMember( d => d.FullName, s => s.FirstName + " " + s.LastName );
	}
}