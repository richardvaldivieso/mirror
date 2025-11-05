using FluentAssertions;
using NSubstitute;
using System.Reflection;

namespace Mirror.Tests
{
	public interface IClock
	{ DateTime Now { get; } }

	public class MirrorTests
	{
		private readonly IMirror _mirror;

		public MirrorTests()
		{
			var cfg = new MirrorConfig();
			cfg.CreateMirror<UserDto, User>( m =>
			{
				m.Ignore( d => d.SecretNote );
				m.ForMember( d => d.FullName, s => s.FirstName + " " + s.LastName );
			} );
			cfg.CreateMirror<AddressDto, Address>();
			cfg.CreateMirror<UserDto, PersonView>( m => m.ForMember( p => p.Age, s => s.Age ) );

			_mirror = cfg.Build();
		}

		[Fact]
		public void Convention_and_overrides_should_map()
		{
			var dto = new UserDto
			{
				FirstName = "Ana",
				LastName = "V",
				Age = 29,
				Home = new AddressDto { Street = "123 Main", City = "Omaha" },
				Tags = new List<string> { "a", "b" },
				Role = Role.Admin
			};

			var user = _mirror.Copy<UserDto, User>( dto );

			user.FirstName.Should().Be( "Ana" );                 // convention
			user.LastName.Should().Be( "V" );                    // convention
			user.Age.Should().Be( 29 );                          // int -> int?
			user.SecretNote.Should().BeNull();                 // ignored
			user.FullName.Should().Be( "Ana V" );                // custom resolver
			user.Home.Should().NotBeNull();
			user.Home!.City.Should().Be( "Omaha" );              // nested map
			user.Tags.Should().BeEquivalentTo( new[] { "a", "b" } ); // collection
			user.Role.Should().Be( nameof( Role.Admin ) );         // enum -> string
		}

		[Fact]
		public void Map_many_should_project_collections()
		{
			var list = new[]
			{
			new UserDto { FirstName="A", LastName="B", Age=1, Home=new AddressDto() },
			new UserDto { FirstName="C", LastName="D", Age=2, Home=new AddressDto() },
		};

			var projected = _mirror.CopyMany<UserDto, User>( list ).ToList();
			projected.Should().HaveCount( 2 );
			projected.Select( x => x.FullName ).Should().BeEquivalentTo( "A B", "C D" );
		}

		[Fact]
		public void Null_source_should_return_default()
		{
			UserDto src = null;
			var dest = _mirror.Copy<UserDto, User>( src );
			dest.Should().BeNull();
		}

		[Fact]
		public void Ctor_only_destination_should_still_map()
		{
			var dto = new UserDto { FirstName = "Jess", LastName = "K", Age = 41 };
			var view = _mirror.Copy<UserDto, PersonView>( dto );

			view.Should().NotBeNull();
			view.Name.Should().BeNull(); // created via best ctor with defaults
			view.Age.Should().Be( 41 );       // later assigned
		}

		[Fact]
		public void String_to_enum_and_nullable_should_work()
		{
			// flip mapping direction to test string->enum and nullable->non-nullable
			var cfg = new MirrorConfig();
			cfg.CreateMirror<User, UserDto>(); // convention: Role string -> Role enum
			var mirror = cfg.Build();

			var user = new User
			{
				FirstName = "Pat",
				LastName = "Lee",
				Age = 33,            // int? -> int (convert)
				Role = "User"
			};

			var dto = mirror.Copy<User, UserDto>( user );
			dto.Age.Should().Be( 33 );
			dto.Role.Should().Be( Role.User );
		}

		[Fact]
		public void Can_use_service_in_resolver_via_closure()
		{
			var clock = Substitute.For<IClock>();
			clock.Now.Returns( new DateTime( 2025, 10, 15 ) );

			var cfg = new MirrorConfig();
			cfg.CreateMirror<UserDto, User>()
			   .ForMember( d => d.FullName, s => s.FirstName + " " + s.LastName + " @" + clock.Now.Year );
			var mirror = cfg.Build();

			var dto = new UserDto { FirstName = "Ana", LastName = "V", Home = new AddressDto() };
			var user = mirror.Copy<User>( dto );

			user.FullName.Should().Be( "Ana V @2025" );
		}

		[Fact]
		public void Compiled_delegate_is_cached_per_type_pair()
		{
			// Arrange
			var cfg = new MirrorConfig();
			cfg.CreateMirror<UserDto, User>();
			cfg.CreateMirror<AddressDto, Address>(); // because User has nested Address
			var engine = (MirrorEngine)cfg.Build();

			var cacheField = typeof( MirrorEngine )
				.GetField( "_compiled", BindingFlags.NonPublic | BindingFlags.Instance )!;
			var cache = (System.Collections.IDictionary)cacheField.GetValue( engine )!;

			cache.Count.Should().Be( 0 );

			// Act 1: first call builds and caches all needed delegates (root + nested)
			engine.Copy<User>( new UserDto
			{
				FirstName = "A",
				Home = new AddressDto { City = "X" },
				Tags = new List<string> { "1", "2" }
			} );

			var countAfterFirst = cache.Count;
			countAfterFirst.Should().BeGreaterThan( 0, "first mapping should compile and cache delegates" );

			// Act 2: second call should reuse existing delegates, not grow the cache
			engine.Copy<UserDto, User>( new UserDto
			{
				FirstName = "B",
				Home = new AddressDto { City = "Y" },
				Tags = new List<string> { "3", "4" }
			} );

			// Assert
			cache.Count.Should().Be( countAfterFirst, "subsequent mappings should reuse cached delegates" );
		}

		[Fact]
		public void Missing_source_members_are_safely_ignored()
		{
			var cfg = new MirrorConfig();
			// Destination has SecretNote; source doesn't. Should not throw.
			cfg.CreateMirror<UserDto, User>();
			var mirror = cfg.Build();

			var dto = new UserDto { FirstName = "A", LastName = "B", Home = new AddressDto() };
			var user = mirror.Copy<User>( dto );

			user.SecretNote.Should().BeNull();
		}

		[Fact]
		public void Reflect_creates_reverse_map_for_bidirectional_copy()
		{
			// Arrange: one-way mirrors, then make them bidirectional via .Reflect()
			var cfg = new MirrorConfig();

			cfg.CreateMirror<UserDto, User>( m =>
			{
				m.Ignore( d => d.SecretNote );
				m.ForMember( d => d.FullName, s => s.FirstName + " " + s.LastName );
			} ).Reflect(); // adds User -> UserDto

			cfg.CreateMirror<AddressDto, Address>()
			   .Reflect(); // adds Address -> AddressDto (needed for nested reverse)

			var mirror = cfg.Build();

			var dto = new UserDto
			{
				FirstName = "Ana",
				LastName = "V",
				Age = 29,
				Home = new AddressDto { Street = "123 Main", City = "Omaha" },
				Tags = new List<string> { "x", "y" },
				Role = Role.Admin
			};

			// Act 1: forward copy (UserDto -> User)
			var user = mirror.Copy<UserDto, User>( dto );

			// Assert forward
			user.FirstName.Should().Be( "Ana" );
			user.LastName.Should().Be( "V" );
			user.Age.Should().Be( 29 );                    // int -> int?
			user.FullName.Should().Be( "Ana V" );          // resolver applied
			user.SecretNote.Should().BeNull();           // ignored
			user.Home.Should().NotBeNull();
			user.Home!.City.Should().Be( "Omaha" );        // nested mapped
			user.Tags.Should().BeEquivalentTo( "x", "y" );
			user.Role.Should().Be( nameof( Role.Admin ) );   // enum -> string

			// Act 2: reverse copy (User -> UserDto), enabled by .Reflect()
			var dto2 = mirror.Copy<User, UserDto>( user );

			// Assert reverse (convention-based fields round-trip)
			dto2.FirstName.Should().Be( "Ana" );
			dto2.LastName.Should().Be( "V" );
			dto2.Age.Should().Be( 29 );                    // int? -> int
			dto2.Home.Should().NotBeNull();
			dto2.Home!.City.Should().Be( "Omaha" );        // nested reverse mapped
			dto2.Tags.Should().BeEquivalentTo( "x", "y" );
			dto2.Role.Should().Be( Role.Admin );           // string -> enum

			// Note: FullName has no counterpart in UserDto; reverse shouldn't try to split it.
		}

		[Fact]
		public void Warmup_example_for_startup()
		{
			var cfg = new MirrorConfig();
			cfg.CreateMirror<UserDto, User>();
			cfg.CreateMirror<AddressDto, Address>();
			cfg.CreateMirror<UserDto, PersonView>();
			cfg.CreateMirror<User, UserDto>();
			cfg.CreateMirror<Address, AddressDto>();
			var mirror = cfg.Build();

			// Warm up by mapping default instances once:
			mirror.Copy<UserDto, User>( new UserDto { Home = new AddressDto() } );
			mirror.Copy<User, UserDto>( new User { Home = new Address() } );
		}
	}
}