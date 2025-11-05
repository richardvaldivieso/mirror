using System.Collections.Concurrent;

namespace Mirror;

public static class MirrorRegistry
{
	private static readonly ConcurrentDictionary<Type, Type> _forward = new(); // src -> dest
	private static readonly ConcurrentDictionary<Type, Type> _reverse = new(); // dest -> src
	private static MirrorEngine _engine;

	// Call once at startup
	public static void Start( Action<MirrorConfig> configure )
	{
		var cfg = new MirrorConfig();
		configure( cfg );
		_engine = cfg.Build();
	}

	// Register a configuration class
	public static void Add<TSource, TDest, TConfig>( this MirrorConfig cfg )
		where TConfig : IConfiguration<TSource, TDest>, new()
	{
		var c = new TConfig();
		var map = cfg.CreateMirror<TSource, TDest>( m => c.Configure( m ) );
		_forward[typeof( TSource )] = typeof( TDest );

		if ( c.Reflect )
		{
			map.Reflect();
			_reverse[typeof( TDest )] = typeof( TSource );
		}
	}

	internal static object CopyAuto( object source )
	{
		if ( source == null ) return null!;
		var sType = source.GetType();

		// forward?
		if ( _forward.TryGetValue( sType, out var dType ) )
			return InvokeEngineCopy( source, dType );

		// reverse?
		if ( _reverse.TryGetValue( sType, out var backType ) )
			return InvokeEngineCopy( source, backType );

		throw new InvalidOperationException(
			$"No MirrorRegistry mapping found for source type '{sType.FullName}'." );
	}

	private static object InvokeEngineCopy( object source, Type dType )
	{
		var open = typeof( MirrorEngine ).GetMethod( nameof( MirrorEngine.Copy ), new[] { typeof( object ) } )!;
		var closed = open.MakeGenericMethod( dType );
		return closed.Invoke( _engine, new[] { source } )!;
	}
}