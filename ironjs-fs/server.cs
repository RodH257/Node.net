/**
* Node.net is a .NET implementation of Node.js and is free 
*	software provided under the MIT license.
*
*	See LICENSE file for full text of the license.
*	Copyright 2010 Dan Newcome.
*/

using System.Threading;
using System.IO;
using System.Text;
using System.Collections;
using System;
using IronJS;
using IronJS.Native;

public class Server 
{
	// temp hack global
	public static Server instance;
	
	// dispatch queue and signalling primitive for main event loop
	private ManualResetEvent manualResetEvent = new ManualResetEvent( false );
	private Queue workItems = new Queue();

	public static void Main() {
		// eval js file given as first commandline arg and 
		// run the event loop - runEventLoop() always blocks, unlike node.js
		// which returns if no more callbacks are registered
		instance = new Server();
		instance.evalCommandlineArgument();
		instance.runEventLoop();
	}
	
	// TODO: finish porting things like require()
	// implements require() for importing js files/namespaces
	/*
	function require( file ) {
		// for compiled-in functionality, return existing references.
		// TODO: clean up the global namespace, `http' et al. are directly visible
		if( file == 'http' ) {
			return http;
		}
		if( file == 'net' ) {
			return net;
		}
		if( file == 'sys' ) {
			return sys;
		}
		print( 'requiring: ' + file );
		var fileContents = readFile( file + '.js' );
		// extra semicolon provided after file contents.. just in case
		var code = 'var exports = {}; ' + fileContents + '; exports;';
		print( 'evaluating require: ' + code );
		return eval( code, 'unsafe' );
	}
	*/

	// threadsafe enqueue function
	public void queueWorkItem( object item ) {
			Console.WriteLine( "queueWorkItem(): queuing item: " + ((Callback)item).name );
			Console.WriteLine( "queueWorkItem(): acquiring lock: " + workItems );
		Monitor.Enter( workItems );
		try {
			workItems.Enqueue( item );
			Console.WriteLine( "queueWorkItem(): item queued" );
			manualResetEvent.Set();
			Console.WriteLine( "queueWorkItem(): queue set to runnable" );
		}
		finally {
			Monitor.Exit( workItems );
			Console.WriteLine( "queueWorkItem: released lock" );
		}
	}

	private string readFile( string filename ) {
		string fileContents;
		StreamReader streamReader = new StreamReader( filename );
		try {
			fileContents = streamReader.ReadToEnd();
			return fileContents;
		}
		finally {
			streamReader.Close();
		}
	}

	public void evalCommandlineArgument() {
		string[] args = System.Environment.GetCommandLineArgs();
		if( args.Length != 2 ) {
			Console.WriteLine( "usage: node <file.js>" );
			System.Environment.Exit(1);
		}
		// string script = readFile( args[1] );
		ReadJsFile( args[1] );
	}

	public void runEventLoop() {
		while( true ) {
			Console.WriteLine( "event loop running" );
			Callback callback = null;
			
			// TODO: can we improve locking method?
			Monitor.Enter( workItems );
			try {
				Console.WriteLine( "event loop: acquired lock" );
				callback = ( Callback )workItems.Dequeue();
				if( callback == null ) {
					manualResetEvent.Reset();
				}
			}
			catch( Exception e ) {
				Console.WriteLine( e );
				manualResetEvent.Reset();
			}
			finally {
				Monitor.Exit( workItems );
				Console.WriteLine( "event loop: released lock" );
			}
			
			if( callback != null ) {
				Console.WriteLine( "event loop: dispatching callback: " + callback.name );
				Console.WriteLine( "event loop: dispatching callback: " + callback.callback );
				Console.WriteLine( "event loop: dispatching args: " + callback.args );
				// TODO: not sure what this callback delegate looks like yet
				callback.callback.Invoke( callback.args );
			}
			Console.WriteLine( "event loop waiting" );
			manualResetEvent.WaitOne();
		} // while
	}
	
	public void ReadJsFile( string in_filename ) {
		IronJS.Hosting.Context ctx = IronJS.Hosting.Context.Create();
		
		// set up 'puts' function
        // Action<object> emit = ( obj ) => { Console.WriteLine( JsTypeConverter.ToString( obj ) ); };
		var emit =
           IronJS.Api.HostFunction.create<Action<IronJS.Box>>( ctx.Environment, ( obj ) => { Console.WriteLine(IronJS.Api.TypeConverter.toString(obj) ); } );
        ctx.PutGlobal("puts", emit );
		
		// Forms the `net" namespace
		net netObj = new net( ctx.Environment );
		ctx.PutGlobal( "net", netObj );
		
		// Forms the `http" namespace
		/*
		http httpObj = new http( ctx.Environment );
		ctx.PutGlobal( "http", httpObj );
		*/
		ctx.ExecuteFile( in_filename );
	}
} // class

// provides the 'net' namespace
class net : IronJS.Object
{
	public net( IronJS.Environment env ) : base( env, env.Maps.Base, env.Prototypes.Object, IronJS.Classes.Object ) {
		// have to set context to satisfy IronJS Obj
		Env = env;
		Methods = Env.Methods.Object;
		
		// SetOwnProperty( "createServer", new Fn_CreateServer( context ) );
		var objMethod = IronJS.Api.HostFunction.create<Func<IronJS.Function, IronJS.Object>>(Env, CreateServer);
		Console.WriteLine( this.Methods == null );
		Console.WriteLine( objMethod );
        this.Methods.PutRefProperty(this, "createServer", objMethod, IronJS.TypeTags.Function);
	}
	
	public IronJS.Object CreateServer( IronJS.Function callback ) {
		Console.WriteLine( "net.createServer() called." );
		Net.NetServer server = new Net.NetServer( callback, Env );
		Console.WriteLine( server );
		return( server );
	}
}
	
// provides the 'http' namespace
/**
class http : IronJS.Object
{
	public http( IronJS.Hosting.Context context ) {
		// have to set context to satisfy IronJS Obj
		Context = context;
		
		SetOwnProperty( "createServer", new Fn_CreateHttpServer( context ) );
	}
}

class Fn_CreateHttpServer : IronJS.Function
{
	public Fn_CreateHttpServer( IronJS.Hosting.Context context ) : base( context ) {}

	public object Call( IronJS.Object that, object[] args ) {
		Console.WriteLine( "http.createServer() called." );
		Http.HttpServer server = new Http.HttpServer( ( UserFunction )args[0], Context );
		Console.WriteLine( server );
		return( server );
	}
}
*/

// note that this encapsulates callbacks on the .net side. The dispatch queue
// is a Queue of these.
// We use IFunction for the js callbacks
public class Callback 
{
	public string name = "";
	public VarDelegate callback;
	public object[] args;
}

public delegate void VarDelegate( params object[] args );
