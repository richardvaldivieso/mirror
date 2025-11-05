namespace Mirror.Tests;

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

public class NamingPolicyTests
{
	// ---- Sample destination (PascalCase) ----
	private sealed class PersonPascal
	{
		public string FirstName { get; set; }
		public string LastName { get; set; }
		public int? Age { get; set; }
		public AddressPascal Home { get; set; }
		public List<PetPascal> Pets { get; set; }
	}

	private sealed class AddressPascal
	{
		public string StreetName { get; set; }
		public string City { get; set; }
	}

	private sealed class PetPascal
	{
		public string PetName { get; set; }
	}

	// ---- Three source shapes with different naming styles ----
	private sealed class PersonSnake
	{
		public string first_name { get; set; }
		public string last_name { get; set; }
		public int age { get; set; }
		public AddressSnake home { get; set; }
		public List<PetSnake> pets { get; set; }
	}

	private sealed class AddressSnake
	{
		public string street_name { get; set; }
		public string city { get; set; }
	}

	private sealed class PetSnake
	{
		public string pet_name { get; set; }
	}

	private sealed class PersonCamel
	{
		public string firstName { get; set; }
		public string lastName { get; set; }
		public int age { get; set; }
		public AddressCamel home { get; set; }
		public List<PetCamel> pets { get; set; }
	}

	private sealed class AddressCamel
	{
		public string streetName { get; set; }
		public string city { get; set; }
	}

	private sealed class PetCamel
	{
		public string petName { get; set; }
	}

	private sealed class PersonScreaming
	{
		public string FIRST_NAME { get; set; }
		public string LAST_NAME { get; set; }
		public int AGE { get; set; }
		public AddressScreaming HOME { get; set; }
		public List<PetScreaming> PETS { get; set; }
	}

	private sealed class AddressScreaming
	{
		public string STREET_NAME { get; set; }
		public string CITY { get; set; }
	}

	private sealed class PetScreaming
	{
		public string PET_NAME { get; set; }
	}

	[Fact]
	public void UniversalToPascalPolicy_maps_various_source_namings_to_PascalCase_destination()
	{
		// Configure Mirror with the universal naming policy
		var cfg = new MirrorConfig()
			.UseNamingPolicy( new UniversalToPascalPolicy() );

		// Element maps for nested/collections (source variants -> Pascal destination)
		cfg.CreateMirror<AddressSnake, AddressPascal>();
		cfg.CreateMirror<AddressCamel, AddressPascal>();
		cfg.CreateMirror<AddressScreaming, AddressPascal>();

		cfg.CreateMirror<PetSnake, PetPascal>();
		cfg.CreateMirror<PetCamel, PetPascal>();
		cfg.CreateMirror<PetScreaming, PetPascal>();

		// Parent maps (three different sources → the same Pascal destination)
		cfg.CreateMirror<PersonSnake, PersonPascal>();
		cfg.CreateMirror<PersonCamel, PersonPascal>();
		cfg.CreateMirror<PersonScreaming, PersonPascal>();

		var mirror = cfg.Build();

		// Arrange three differently-named sources
		var snake = new PersonSnake
		{
			first_name = "Ana",
			last_name = "V",
			age = 29,
			home = new AddressSnake { street_name = "123 Main", city = "Omaha" },
			pets = new List<PetSnake> { new PetSnake { pet_name = "Rex" } }
		};

		var camel = new PersonCamel
		{
			firstName = "Ben",
			lastName = "K",
			age = 31,
			home = new AddressCamel { streetName = "5th Ave", city = "NYC" },
			pets = new List<PetCamel> { new PetCamel { petName = "Milo" }, new PetCamel { petName = "Luna" } }
		};

		var scream = new PersonScreaming
		{
			FIRST_NAME = "CHRIS",
			LAST_NAME = "P",
			AGE = 40,
			HOME = new AddressScreaming { STREET_NAME = "OAK", CITY = "AUSTIN" },
			PETS = new List<PetScreaming> { new PetScreaming { PET_NAME = "PIXEL" } }
		};

		// Act (use destination-only overload to prove inference works with the policy)
		var p1 = mirror.Copy<PersonPascal>( snake );
		var p2 = mirror.Copy<PersonPascal>( camel );
		var p3 = mirror.Copy<PersonPascal>( scream );

		// Assert snake_case -> PascalCase
		p1.FirstName.Should().Be( "Ana" );
		p1.LastName.Should().Be( "V" );
		p1.Age.Should().Be( 29 );
		p1.Home.Should().NotBeNull();
		p1.Home!.StreetName.Should().Be( "123 Main" );
		p1.Home.City.Should().Be( "Omaha" );
		p1.Pets.Should().HaveCount( 1 );
		p1.Pets!.First().PetName.Should().Be( "Rex" );

		// Assert camelCase -> PascalCase
		p2.FirstName.Should().Be( "Ben" );
		p2.LastName.Should().Be( "K" );
		p2.Age.Should().Be( 31 );
		p2.Home!.StreetName.Should().Be( "5th Ave" );
		p2.Home.City.Should().Be( "NYC" );
		p2.Pets!.Select( x => x.PetName ).Should().BeEquivalentTo( "Milo", "Luna" );

		// Assert SCREAMING_SNAKE -> PascalCase (acronyms normalized)
		p3.FirstName.Should().Be( "CHRIS" ); // value preserved; only member names normalized
		p3.LastName.Should().Be( "P" );
		p3.Age.Should().Be( 40 );
		p3.Home!.StreetName.Should().Be( "OAK" );
		p3.Home.City.Should().Be( "AUSTIN" );
		p3.Pets!.First().PetName.Should().Be( "PIXEL" );
	}
}