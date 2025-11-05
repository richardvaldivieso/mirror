namespace Mirror;

public interface IConfiguration<TSource, TDest>
{
	void Configure( MirrorMap<TSource, TDest> map );

	bool Reflect { get; }  // if true, auto-register reverse map too
}