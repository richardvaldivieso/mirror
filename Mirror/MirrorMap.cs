using System.Linq.Expressions;

namespace Mirror;

public sealed class MirrorMap<TSource, TDest> : IMapPlan
{
	/// <summary>
	/// Back-reference to the owning configuration, used by extension methods like Reflect().
	/// </summary>
	internal readonly MirrorConfig Parent;

	/// <summary>
	/// Destination member names to ignore.
	/// </summary>
	internal readonly HashSet<string> Ignored = new( StringComparer.Ordinal );

	/// <summary>
	/// Custom resolvers for destination members (keyed by destination member name).
	/// </summary>
	internal readonly Dictionary<string, LambdaExpression> Resolvers = new( StringComparer.Ordinal );

	/// <summary>
	/// Internal ctor used by MirrorConfig so maps know their parent.
	/// </summary>
	internal MirrorMap( MirrorConfig parent ) => Parent = parent;

	/// <summary>
	/// Ignore a destination member.
	/// </summary>
	public MirrorMap<TSource, TDest> Ignore<TMember>( Expression<Func<TDest, TMember>> destMember )
	{
		Ignored.Add( GetMemberName( destMember ) );
		return this;
	}

	/// <summary>
	/// Provide a custom resolver for a destination member (source &rarr; dest).
	/// </summary>
	public MirrorMap<TSource, TDest> ForMember<TMember>(
		Expression<Func<TDest, TMember>> destMember,
		Expression<Func<TSource, TMember>> resolver )
	{
		Resolvers[GetMemberName( destMember )] = resolver ?? throw new ArgumentNullException( nameof( resolver ) );
		return this;
	}

	private static string GetMemberName<TMember>( Expression<Func<TDest, TMember>> expr )
	{
		if ( expr is null ) throw new ArgumentNullException( nameof( expr ) );

		if ( expr.Body is MemberExpression m ) return m.Member.Name;
		if ( expr.Body is UnaryExpression u && u.Operand is MemberExpression um ) return um.Member.Name;

		throw new ArgumentException( "Unsupported member expression. Use a simple member access like d => d.Property.", nameof( expr ) );
	}
}