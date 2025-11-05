namespace Mirror;

public interface IMirror
{
	TDest Copy<TSource, TDest>( TSource source );

	IEnumerable<TDest> CopyMany<TSource, TDest>( IEnumerable<TSource> source );
}