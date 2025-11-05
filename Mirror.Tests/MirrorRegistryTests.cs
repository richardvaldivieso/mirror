using FluentAssertions;

namespace Mirror.Tests;

public class MirrorRegistryTests
{
	public MirrorRegistryTests()
	{
		// Start the registry once per test (safe to re-run)
		MirrorRegistry.Start( cfg =>
		{
			cfg.Add<UserDto, User, UserConfiguration>();
			cfg.Add<AddressDto, Address, AddressConfiguration>();
		} );
	}

	[Fact]
	public void MirrorCopy_works_forward_and_reverse_with_registry_configs()
	{
		var dto = new UserDto
		{
			FirstName = "Ana",
			LastName = "V",
			Age = 29,
			Role = Role.Admin,
			Home = new AddressDto { Street = "123 Main", City = "Omaha" },
			Tags = new List<string> { "x", "y" }
		};

		// Forward: dto -> user (no generics, no engine param)
		var user = (User)dto.MirrorCopy();

		user.FullName.Should().Be( "Ana V" );
		user.Role.Should().Be( nameof( Role.Admin ) );        // enum -> string
		user.Age.Should().Be( 29 );
		user.SecretNote.Should().BeNull();                // ignored
		user.Home.Should().NotBeNull();
		user.Home!.City.Should().Be( "Omaha" );
		user.Tags.Should().BeEquivalentTo( "x", "y" );

		// Reverse: user -> dto (registered because Reflect=true)
		var dto2 = (UserDto)user.MirrorCopy();

		dto2.FirstName.Should().Be( "Ana" );
		dto2.LastName.Should().Be( "V" );
		dto2.Role.Should().Be( Role.Admin );               // string -> enum
		dto2.Home.Should().NotBeNull();
		dto2.Home!.City.Should().Be( "Omaha" );
		dto2.Tags.Should().BeEquivalentTo( "x", "y" );
	}

	[Fact]
	public void MirrorCopy_throws_for_unregistered_source_type()
	{
		var ghost = new Ghost(); // not registered anywhere

		Action act = () => ghost.MirrorCopy(); // should look up type and fail
		act.Should().Throw<InvalidOperationException>()
		   .WithMessage( "*No MirrorRegistry mapping found for source type*" );
	}

	[Fact]
	public void Explicit_engine_and_registry_results_should_match()
	{
		// Build an explicit engine with the same configuration
		var cfg = new MirrorConfig();
		cfg.CreateMirror<UserDto, User>( m =>
		{
			m.Ignore( d => d.SecretNote );
			m.ForMember( d => d.FullName, s => s.FirstName + " " + s.LastName );
		} );
		cfg.CreateMirror<AddressDto, Address>();
		var mirror = cfg.Build();

		var dto = new UserDto
		{
			FirstName = "Pat",
			LastName = "Lee",
			Age = 33,
			Role = Role.User,
			Home = new AddressDto { City = "Nashville" },
			Tags = new List<string> { "a", "b" }
		};

		var viaRegistry = (User)dto.MirrorCopy();
		var viaEngine = mirror.Copy<User>( dto );

		viaEngine.FullName.Should().Be( viaRegistry.FullName );
		viaEngine.Role.Should().Be( viaRegistry.Role );
		viaEngine.Age.Should().Be( viaRegistry.Age );
		viaEngine.Home!.City.Should().Be( viaRegistry.Home!.City );
		viaEngine.Tags.Should().BeEquivalentTo( viaRegistry.Tags );
	}

	[Fact]
	public void UniversalToPascalPolicy_examples()
	{
		var p = new UniversalToPascalPolicy();

		p.NormalizeSource( "first_name" ).Should().Be( "FirstName" );
		p.NormalizeSource( "first-name" ).Should().Be( "FirstName" );
		p.NormalizeSource( "first name" ).Should().Be( "FirstName" );
		p.NormalizeSource( "firstName" ).Should().Be( "FirstName" );
		p.NormalizeSource( "FirstName" ).Should().Be( "FirstName" );
		p.NormalizeSource( "FIRST_NAME" ).Should().Be( "FirstName" );
		p.NormalizeSource( "HTTP_server" ).Should().Be( "HttpServer" );
		p.NormalizeSource( "user2id" ).Should().Be( "User2Id" );
		p.NormalizeSource( "2fa_enabled" ).Should().Be( "2FaEnabled" );
	}

	private sealed class Ghost
	{ public string Name { get; set; } }
}