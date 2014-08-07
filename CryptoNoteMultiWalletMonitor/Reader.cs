using System;

using System.Threading;

namespace CryptoNoteMultiWalletMonitor
{
	internal static class Reader
	{
		private static Thread mInputThread = new Thread( GetInput );
		private static AutoResetEvent mGetInput = new AutoResetEvent( false );
		private static AutoResetEvent mGotInput = new AutoResetEvent( false );
		private static string msInput = "";

		[System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline" )]
		static Reader( )
		{
			mInputThread.IsBackground = true;
			mInputThread.Start( );
		}

		private static void GetInput( )
		{
			while( true )
			{
				mGetInput.WaitOne( );
				msInput = Console.ReadLine( );
				mGotInput.Set( );
			}
		}

		public static string ReadLine( int lnTimeOutMilliSeconds )
		{
			mGetInput.Set( );
			bool lbSuccess = mGotInput.WaitOne( lnTimeOutMilliSeconds );
			if( lbSuccess ) return msInput;
			else
			{
				//while( Console.KeyAvailable )
				throw new TimeoutException( "Reader timeout expired." );
			}
		}
	}
}