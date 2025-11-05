namespace Mirror;

// Convenience: object.MirrorTo<TDest>(IMirror mirror)
public static class MirrorExtensions
{
	public static TDest MirrorTo<TDest>( this object source, IMirror mirror )
		=> mirror.Copy<object, TDest>( source );

	public static MirrorConfig Reflect<TSource, TDest>( this MirrorMap<TSource, TDest> forward )
	{
		if ( forward is null ) throw new ArgumentNullException( nameof( forward ) );
		var cfg = forward.Parent ?? throw new InvalidOperationException( "Map is not attached to a MirrorConfig." );

		// Create reverse plan and carry over simple rules (ignores).
		var reverse = new MirrorMap<TDest, TSource>( cfg );
		foreach ( var name in forward.Ignored )
			reverse.Ignored.Add( name );

		// NOTE: resolvers aren't automatically invertible; not copied.

		cfg.Register( typeof( TDest ), typeof( TSource ), reverse );
		return cfg;
	}

	/// <summary>
	/// Copies this object to its configured counterpart type (as registered in MirrorRegistry).
	/// Returns 'object' — cast to your expected type or use pattern matching.
	/// </summary>
	public static object MirrorCopy( this object source )
		=> MirrorRegistry.CopyAuto( source );
}