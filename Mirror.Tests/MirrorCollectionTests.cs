using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mirror.Tests;

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

public class MirrorCollectionMappingTests
{
	// Minimal models just for this test
	private sealed class ChildDto
	{ public string Name { get; set; } }

	private sealed class ParentDto
	{ public List<ChildDto> Kids { get; set; } }

	private sealed class Child
	{ public string Name { get; set; } }

	private sealed class Parent
	{ public List<Child> Kids { get; set; } }

	[Fact]
	public void Collection_of_different_object_types_maps_forward_and_reverse()
	{
		// Arrange: element + parent mirrors; make both bidirectional
		var cfg = new MirrorConfig();

		cfg.CreateMirror<ChildDto, Child>().Reflect();
		cfg.CreateMirror<ParentDto, Parent>().Reflect();

		var mirror = cfg.Build();

		var dto = new ParentDto
		{
			Kids = new List<ChildDto>
			{
				new ChildDto { Name = "Ava" },
				new ChildDto { Name = "Milo" }
			}
		};

		// Act 1: forward (ParentDto -> Parent)
		var parent = mirror.Copy<Parent>( dto );

		// Assert forward
		parent.Should().NotBeNull();
		parent.Kids.Should().NotBeNull();
		parent.Kids!.Select( k => k.Name ).Should().BeEquivalentTo( "Ava", "Milo" );

		// Act 2: reverse (Parent -> ParentDto)
		var dto2 = mirror.Copy<ParentDto>( parent );

		// Assert reverse
		dto2.Should().NotBeNull();
		dto2.Kids.Should().NotBeNull();
		dto2.Kids!.Select( k => k.Name ).Should().BeEquivalentTo( "Ava", "Milo" );
	}
}