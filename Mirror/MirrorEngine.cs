using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

using Mirror;

public sealed class MirrorEngine : IMirror
{
	private readonly MirrorConfig _config;

	// Cache of compiled typed delegates: Func<TSource, MirrorEngine, TDest>
	private readonly ConcurrentDictionary<(Type, Type), Delegate> _compiled = new();

	// Tiny adapter cache so Copy<TDest>(object) avoids DynamicInvoke
	private readonly ConcurrentDictionary<(Type, Type), Func<object, MirrorEngine, object>> _adapters = new();

	public MirrorEngine( MirrorConfig config ) => _config = config;

	// --- Public API ----------------------------------------------------------

	public TDest Copy<TSource, TDest>( TSource source )
	{
		if ( source == null ) return default!;
		var del = (Func<TSource, MirrorEngine, TDest>)_compiled.GetOrAdd(
			(typeof( TSource ), typeof( TDest )),
			_ => Build<TSource, TDest>() );
		return del( source, this );
	}

	public IEnumerable<TDest> CopyMany<TSource, TDest>( IEnumerable<TSource> source )
	{
		if ( source == null ) yield break;
		foreach ( var item in source ) yield return Copy<TSource, TDest>( item );
	}

	// Ergonomic overload: infer source type at runtime, specify only TDest
	public TDest Copy<TDest>( object source )
	{
		if ( source == null ) return default!;
		var sType = source.GetType();
		var key = (sType, typeof( TDest ));

		var adapter = _adapters.GetOrAdd( key, k =>
		{
			// Ensure we have the typed mapping delegate in _compiled
			var typedDel = _compiled.GetOrAdd( k, _ => BuildGeneric( k.Item1, k.Item2 ) );

			// Build: (object s, MirrorEngine me) => (object)((Func<TS,MirrorEngine,TD>)typedDel)((TS)s, me)
			var objParam = Expression.Parameter( typeof( object ), "s" );
			var engParam = Expression.Parameter( typeof( MirrorEngine ), "me" );

			var ts = k.Item1;
			var td = k.Item2;
			var typedFuncType = typeof( Func<,,> ).MakeGenericType( ts, typeof( MirrorEngine ), td );

			var castSrc = Expression.Convert( objParam, ts );
			var delConst = Expression.Constant( typedDel, typedFuncType );
			var invoke = Expression.Invoke( delConst, castSrc, engParam );
			var boxed = Expression.Convert( invoke, typeof( object ) );

			var lambda = Expression.Lambda<Func<object, MirrorEngine, object>>( boxed, objParam, engParam );
			return lambda.Compile();
		} );

		return (TDest)adapter( source, this );
	}

	// --- Builder -------------------------------------------------------------

	private Delegate BuildGeneric( Type sType, Type dType )
	{
		// Find Build<TSource, TDest>() and close it with runtime types
		var open = typeof( MirrorEngine ).GetMethod( nameof( Build ), BindingFlags.NonPublic | BindingFlags.Instance );
		var closed = open!.MakeGenericMethod( sType, dType );
		// Build<TS,TD>() returns a Func<TS, MirrorEngine, TD>
		return (Delegate)closed.Invoke( this, null )!;
	}

	private Func<TSource, MirrorEngine, TDest> Build<TSource, TDest>()
	{
		var sType = typeof( TSource );
		var dType = typeof( TDest );

		_config.TryGetPlan( sType, dType, out var planObj );
		var plan = ( planObj as MirrorMap<TSource, TDest> ) ?? new MirrorMap<TSource, TDest>( _config );

		var src = Expression.Parameter( sType, "src" );
		var eng = Expression.Parameter( typeof( MirrorEngine ), "mirror" );
		var dest = Expression.Variable( dType, "dest" );

		var ctor = PickCtor( dType );
		var init = Expression.Assign( dest, ctor );
		var steps = new List<Expression> { init };

		var policy = _config.NamingPolicy;

		var destMembers = SettableMembers( dType );
		// Source lookup keyed by *normalized* source names
		var srcMembers = GettableMembers( sType )
			.ToDictionary( m => policy.NormalizeSource( m.Name ), m => m, StringComparer.Ordinal );

		foreach ( var d in destMembers )
		{
			if ( plan.Ignored.Contains( d.Name ) ) continue;

			Expression value;

			if ( plan.Resolvers.TryGetValue( d.Name, out var resolver ) )
			{
				// ForMember is explicitly bound to the destination member; just invoke + convert
				value = ConvertIfNeeded( Expression.Invoke( resolver, src ), MemberType( d ) );
			}
			else
			{
				// Normalize the destination name before looking up the source member
				var key = policy.NormalizeDestination( d.Name );
				if ( !srcMembers.TryGetValue( key, out var s ) ) continue;

				var sExpr = Expression.MakeMemberAccess( src, s );
				var sTypeM = MemberType( s );
				var dTypeM = MemberType( d );

				if ( IsSimpleAssignable( sTypeM, dTypeM ) )
				{
					value = ConvertIfNeeded( sExpr, dTypeM );
				}
				else
				{
					value = BuildNested( sExpr, sTypeM, dTypeM, eng );
					if ( value == null ) continue;
				}
			}

			steps.Add( Assign( dest, d, value ) );
		}

		steps.Add( dest );
		var body = Expression.Block( new[] { dest }, steps );
		return Expression.Lambda<Func<TSource, MirrorEngine, TDest>>( body, src, eng ).Compile();
	}

	// --- Helpers: construction & reflection ---------------------------------

	private static NewExpression PickCtor( Type t )
	{
		var defaultCtor = t.GetConstructor( Type.EmptyTypes );
		if ( defaultCtor != null ) return Expression.New( defaultCtor );

		var best = t.GetConstructors().OrderByDescending( c => c.GetParameters().Length ).FirstOrDefault()
				   ?? throw new InvalidOperationException( $"No usable constructor for {t.Name}." );
		var args = best.GetParameters().Select( p => Expression.Default( p.ParameterType ) );
		return Expression.New( best, args );
	}

	private static IEnumerable<MemberInfo> SettableMembers( Type t ) =>
		t.GetMembers( BindingFlags.Public | BindingFlags.Instance )
		 .Where( m => ( m is PropertyInfo p && p.CanWrite ) || ( m is FieldInfo f && !f.IsInitOnly ) );

	private static IEnumerable<MemberInfo> GettableMembers( Type t ) =>
		t.GetMembers( BindingFlags.Public | BindingFlags.Instance )
		 .Where( m => ( m is PropertyInfo p && p.CanRead ) || ( m is FieldInfo ) );

	private static Type MemberType( MemberInfo m ) =>
		m is PropertyInfo p ? p.PropertyType :
		m is FieldInfo f ? f.FieldType :
		throw new NotSupportedException();

	private static BinaryExpression Assign( Expression target, MemberInfo member, Expression value ) =>
		member is PropertyInfo p ? Expression.Assign( Expression.Property( target, p ), value )
	  : member is FieldInfo f ? Expression.Assign( Expression.Field( target, f ), value )
	  : throw new NotSupportedException();

	// --- Helpers: assignability & conversion --------------------------------

	private static bool IsSimpleAssignable( Type src, Type dest )
	{
		if ( dest.IsAssignableFrom( src ) ) return true;

		// Nullable<T> target
		if ( Nullable.GetUnderlyingType( dest ) is Type ud && ud.IsAssignableFrom( src ) ) return true;

		// Nullable<T> source -> T dest (including numeric widening)
		if ( Nullable.GetUnderlyingType( src ) is Type us && ( dest == us || CanWiden( us, dest ) ) )
			return true;

		// enums <-> string/underlying
		if ( dest.IsEnum && ( src == Enum.GetUnderlyingType( dest ) || src == typeof( string ) ) ) return true;
		if ( src.IsEnum && ( dest == Enum.GetUnderlyingType( src ) || dest == typeof( string ) ) ) return true;

		// IEnumerable<S> -> IEnumerable<D> is handled elsewhere (not "simple")
		return false;
	}

	private static bool CanWiden( Type from, Type to )
	{
		try
		{
			_ = Expression.Convert( Expression.Parameter( from, "_" ), to );
			return true;
		}
		catch { return false; }
	}

	private static Expression ConvertIfNeeded( Expression expr, Type to )
	{
		if ( expr.Type == to ) return expr;

		// Nullable<T> TARGET (handle string->Nullable<Enum> specially)
		var toUnderlying = Nullable.GetUnderlyingType( to );
		if ( toUnderlying != null )
		{
			if ( expr.Type == typeof( string ) && toUnderlying.IsEnum )
			{
				var isNull = Expression.Equal( expr, Expression.Constant( null, typeof( string ) ) );

				var parse = typeof( Enum ).GetMethod( nameof( Enum.Parse ), new[] { typeof( Type ), typeof( string ), typeof( bool ) } )!;
				var parsedObj = Expression.Call( parse, Expression.Constant( toUnderlying ), expr, Expression.Constant( true ) );
				var parsedEnum = Expression.Convert( parsedObj, toUnderlying );

				var ctor = to.GetConstructor( new[] { toUnderlying } )!;
				var some = Expression.New( ctor, parsedEnum );

				return Expression.Condition( isNull, Expression.Default( to ), some );
			}

			// general path: convert to underlying then wrap
			var converted = ConvertIfNeeded( expr, toUnderlying );
			if ( converted.Type != toUnderlying )
				converted = Expression.Convert( converted, toUnderlying );
			var wrapCtor = to.GetConstructor( new[] { toUnderlying } )!;
			return Expression.New( wrapCtor, converted );
		}

		// Nullable<T> SOURCE -> non-nullable T  (null => default(T))
		var fromUnderlying = Nullable.GetUnderlyingType( expr.Type );
		if ( fromUnderlying != null && ( to == fromUnderlying || CanWiden( fromUnderlying, to ) ) )
		{
			var hasValue = Expression.Property( expr, "HasValue" );
			var value = Expression.Property( expr, "Value" );
			var conv = value.Type == to ? (Expression)value : Expression.Convert( value, to );
			return Expression.Condition( hasValue, conv, Expression.Default( to ) );
		}

		// string -> enum (null => default(TEnum))
		if ( to.IsEnum && expr.Type == typeof( string ) )
		{
			var isNull = Expression.Equal( expr, Expression.Constant( null, typeof( string ) ) );

			var parse = typeof( Enum ).GetMethod( nameof( Enum.Parse ), new[] { typeof( Type ), typeof( string ), typeof( bool ) } )!;
			var parsedObj = Expression.Call( parse, Expression.Constant( to ), expr, Expression.Constant( true ) );
			var parsedEnum = Expression.Convert( parsedObj, to );

			return Expression.Condition( isNull, Expression.Default( to ), parsedEnum );
		}

		// enum -> string
		if ( expr.Type.IsEnum && to == typeof( string ) )
		{
			var toString = typeof( object ).GetMethod( nameof( ToString ) )!;
			return Expression.Call( Expression.Convert( expr, typeof( object ) ), toString );
		}

		// fallback convert
		return Expression.Convert( expr, to );
	}

	// --- Helpers: nested & collections --------------------------------------

	private static bool IsEnumerableType( Type t, out Type elem )
	{
		elem = null!;
		if ( t == typeof( string ) ) return false;
		var ienum = t.GetInterfaces().Concat( new[] { t } )
			.FirstOrDefault( i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof( IEnumerable<> ) );
		if ( ienum == null ) return false;
		elem = ienum.GetGenericArguments()[0];
		return true;
	}

	private Expression BuildNested( Expression srcExpr, Type sType, Type dType, ParameterExpression eng )
	{
		// IEnumerable<S> -> IEnumerable<D>
		if ( IsEnumerableType( sType, out var sElem ) && IsEnumerableType( dType, out var dElem ) )
		{
			var copyManyClosed = OpenCopyMany2.MakeGenericMethod( sElem, dElem );
			var call = Expression.Call( eng, copyManyClosed, srcExpr );

			if ( dType.IsGenericType && dType.GetGenericTypeDefinition() == typeof( List<> ) )
			{
				var toList = typeof( Enumerable ).GetMethod( nameof( Enumerable.ToList ) )!.MakeGenericMethod( dElem );
				return Expression.Call( toList, call );
			}
			return call; // IEnumerable<D>
		}

		// Complex type: call Copy<S, D>(S)
		var copyClosed = OpenCopy2.MakeGenericMethod( sType, dType );
		return Expression.Call( eng, copyClosed, srcExpr );
	}

	private static readonly MethodInfo OpenCopy2 = typeof( MirrorEngine )
	.GetMethods( BindingFlags.Instance | BindingFlags.Public )
	.First( m => m.Name == nameof( Copy ) && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2 );

	private static readonly MethodInfo OpenCopyMany2 = typeof( MirrorEngine )
		.GetMethods( BindingFlags.Instance | BindingFlags.Public )
		.First( m => m.Name == nameof( CopyMany ) && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2 );
}