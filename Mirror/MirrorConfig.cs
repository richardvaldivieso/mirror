using System.Collections.Concurrent;

namespace Mirror;

public sealed class MirrorConfig
{
	private readonly ConcurrentDictionary<(Type, Type), IMapPlan> _plans = new();

	internal void Register( Type s, Type d, IMapPlan plan ) => _plans[(s, d)] = plan;

	// ✅ Add this back:
	internal bool TryGetPlan( Type s, Type d, out IMapPlan plan )
		=> _plans.TryGetValue( (s, d), out plan );

	internal INamingPolicy NamingPolicy { get; private set; } = new UniversalToPascalPolicy();

	public MirrorConfig UseNamingPolicy( INamingPolicy policy )
	{
		NamingPolicy = policy ?? new UniversalToPascalPolicy();
		return this;
	}

	public MirrorMap<TSource, TDest> CreateMirror<TSource, TDest>()
	{
		var map = new MirrorMap<TSource, TDest>( this );
		_plans[(typeof( TSource ), typeof( TDest ))] = map;
		return map;
	}

	public MirrorMap<TSource, TDest> CreateMirror<TSource, TDest>(
		Action<MirrorMap<TSource, TDest>> config )
	{
		var map = new MirrorMap<TSource, TDest>( this );
		_plans[(typeof( TSource ), typeof( TDest ))] = map;
		config?.Invoke( map );
		return map;
	}

	public MirrorEngine Build() => new( this );
}