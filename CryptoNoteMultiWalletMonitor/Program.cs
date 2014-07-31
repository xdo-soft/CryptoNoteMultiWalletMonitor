using CryptoNoteMultiWalletMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace CryptoNoteMultiWalletMonitor
{
	internal static class Program
	{
		private static Dictionary<int, String> mOutputData = new Dictionary<int, String>( );
		private static Dictionary<int, Decimal> mBalances = new Dictionary<int, Decimal>( );
		private static Dictionary<int, Decimal> mUnlockedBalances = new Dictionary<int, Decimal>( );
		private static Dictionary<int, object> mLocks = new Dictionary<int, object>( );
		private static Dictionary<string, Process> mProcesses = new Dictionary<string, Process>( );

		private static IEnumerable<string> masWalletFullPaths = System.IO.Directory.EnumerateFiles( Settings.Default.WalletDirectory, Settings.Default.WalletFilter );
		private static List<String> masWallets = new List<String>( );
		private static int mnWalletNameLength = 0;
		private static string msPassword = "";
		private static bool mbIsPaused = false;
		private static Object mConsoleLock = new Object( );

		private static void Main( string[ ] args )
		{
			lock( mConsoleLock )
			{
				AssemblyName lAssemblyName = Assembly.GetExecutingAssembly( ).GetName( );
				Console.WriteLine( lAssemblyName.Name.ToString( ) + ", v" + lAssemblyName.Version.ToString( ) );
			}
			if( Settings.Default.SettingsUpgradeRequired )
			{
				Settings.Default.Upgrade( );
				Settings.Default.SettingsUpgradeRequired = false;
			}
			Settings.Default.Save( );
			if( args.Length > 0 )
			{
				string lsFirstArgument = args[0].Trim( );
				string lsFirstArgumentLower = lsFirstArgument.ToUpperInvariant( ) ;
				if( lsFirstArgumentLower == "HELP" || lsFirstArgumentLower == "-HELP" || lsFirstArgumentLower == "--HELP" || lsFirstArgumentLower == "/HELP" || lsFirstArgument == "/?" || lsFirstArgument == "-?" )
				{
					lock( mConsoleLock )
					{
						Console.WriteLine( "Usage: CryptoNoteMultiMonitor [WalletPassword]" );
					}
				}
				msPassword = lsFirstArgument;
			}
			if( msPassword.Length == 0 )
			{
				if( Settings.Default.Password.Length == 0 )
				{
					lock( mConsoleLock ) Console.WriteLine( "Please enter your wallet password:" );
					msPassword = Console.ReadLine( );
				}
				else msPassword = Settings.Default.Password;
			}
			foreach( string lsWalletFullPath in masWalletFullPaths )
			{
				string lsWallet = System.IO.Path.GetFileName( lsWalletFullPath );
				masWallets.Add( lsWallet );
				if( lsWallet.Length > mnWalletNameLength ) mnWalletNameLength = lsWallet.Length;

				StartProcess( lsWallet );
			}
			Thread lMonitorThread = new Thread( MonitorOutput );
			PushCommandToAll( "refresh" );
			PushCommandToAll( "save" );
			PushCommandToAll( "balance" );
			lMonitorThread.Start( );
			while( lMonitorThread.IsAlive )
			{
				String lsCommand;
				try
				{
					if( mbIsPaused ) lsCommand = Reader.ReadLine( Int32.MaxValue );
					else lsCommand = Reader.ReadLine( ( int ) Math.Min( Int32.MaxValue, Settings.Default.RefreshIntervalSeconds * 1000 ) ).Trim( );
				}
				catch
				{
					lsCommand = "refresh";
				}
				switch( lsCommand )
				{
				case "exit":
					PushCommandToAll( "save" );
					Settings.Default.Save( );
					PushCommandToAll( "exit" );
					lMonitorThread.Join( );
					return;

				case "save":
					PushCommandToAll( "save" );
					Settings.Default.Save( );
					break;

				case "refresh":
					Refresh( );
					break;

				case "help":
					lock( mConsoleLock )
					{
						Console.WriteLine( "Commands for CryptoNoteMultiMonitor include:" );
						Console.WriteLine( "pause                                 Temporarily pauses automatic refreshing." );
						Console.WriteLine( "unpause                               Unpauses automatic refreshing." );
						Console.WriteLine( "<command>                             Sends the command <command> to all child wallets. See below for supported child wallet commands." );
						Console.WriteLine( "<walletfilename> <command>            Sends the command <command> to the child wallet <walletfilename>. See below for supported child wallet commands." );
						DisplaySetHelp( );
						Console.WriteLine( "Commands for the child wallets include:" );
					}
					PushCommand( "help", masWallets[ 1 ] );
					break;

				case "pause":
					mbIsPaused = true;
					lock( mConsoleLock ) Console.WriteLine( "Automatic refreshing paused." );
					break;

				case "unpause":
					mbIsPaused = false;
					lock( mConsoleLock ) Console.WriteLine( "Automatic refreshing unpaused." );
					Refresh( );
					break;

				case "":
					break;

				default:
					bool lbToAll = CommandIsForAll( lsCommand );

					if( lbToAll ) PushCommandToAll( lsCommand );
					break;
				}
			}
		}

		private static void Refresh( )
		{
			PushCommandToAll( "refresh" );
			PushCommandToAll( "save" );
			Settings.Default.Save( );
			PushCommandToAll( "balance" );
		}

		private static bool CommandIsForAll( String lsCommand )
		{
			if( lsCommand == "set" )
			{
				lock( mConsoleLock ) DisplaySetHelp( );
				return false;
			}
			if( lsCommand.Length > 4 && lsCommand.Substring( 0, 4 ) == "set " )
			{
				lsCommand = lsCommand.Substring( 4 ).Trim( );
				int lnSpaceIndex = lsCommand.IndexOf( ' ' );
				if( lnSpaceIndex < 0 )
				{
					lock( mConsoleLock ) DisplaySetHelp( );
					return false;
				}
				string lsToSet = lsCommand.Substring( 0, lnSpaceIndex ).Trim( );
				string lsValue = lsCommand.Substring( lnSpaceIndex + 1 ).Trim( );
				try
				{
					lock( Settings.Default )
					{
						Type lType = Settings.Default[ lsToSet ].GetType( );
						// Console.WriteLine( lType.ToString( ) );
						Settings.Default[ lsToSet ] = Convert.ChangeType( lsValue, lType, CultureInfo.CurrentCulture );
						Settings.Default.Save( );
					}
				}
				catch( Exception lException )
				{
					lock( mConsoleLock )
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine( lException.ToString( ) );
						Console.ResetColor( );
					}
				}
				return false;
			}

			int lnEndFirstWord;
			if( lsCommand[ 0 ] == '"' )
			{
				lsCommand = lsCommand.Substring( 1 );
				lnEndFirstWord = lsCommand.IndexOf( '"' );
			}
			else lnEndFirstWord = lsCommand.IndexOf( ' ' );
			if( lnEndFirstWord > 0 )
			{
				string lsTargetWallet = lsCommand.Substring( 0, lnEndFirstWord ).Trim( );
				if( masWallets.Contains( lsTargetWallet ) && lsCommand.Length > lnEndFirstWord + 1 )
				{
					lsCommand = lsCommand.Substring( lnEndFirstWord + 1 ).Trim( );
					if( lsCommand.Length > 0 )
					{
						PushCommand( lsCommand, lsTargetWallet );
						return false;
					}
				}
			}
			return true;
		}

		private static void DisplaySetHelp( )
		{
			// deliberately does not lock the console
			Console.WriteLine( "set <PropertyName> <PropertyValue>    Sets the property <PropertyName> of CryptoNoteMultiWalletMonitor to the value <PropertyValue>." );
			Console.WriteLine( "PropertyName may be any of the following:" );
			foreach( SettingsProperty lProperty in Settings.Default.Properties )
			{
				Console.WriteLine( "\t" + lProperty.Name );
			}
		}

		private static void PushCommandToAll( String lsCommand )
		{
			foreach( string lsWallet in masWallets )
			{
				PushCommand( lsCommand, lsWallet );
			}
		}

		private static void PushCommand( String lsCommand, string lsWallet )
		{
			if( !mProcesses.ContainsKey( lsWallet ) ) StartProcess( lsWallet );
			Process lProcess = mProcesses[ lsWallet ];
			lProcess.StandardInput.WriteLine( lsCommand );
			// mProcesses[ lsWallet ] = lProcess;
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Reliability", "CA2000:Dispose objects before losing scope" )]
		private static void StartProcess( string lsWallet )
		{
			ProcessStartInfo lPSI = new ProcessStartInfo( Settings.Default.CryptoNoteDirectory + Settings.Default.WalletCommand, "--wallet-file " + lsWallet + " --password " + msPassword );
			lPSI.CreateNoWindow = true;
			lPSI.ErrorDialog = true;
			lPSI.RedirectStandardError = true;
			lPSI.RedirectStandardInput = true;
			lPSI.RedirectStandardOutput = true;
			lPSI.UseShellExecute = false;
			lPSI.WorkingDirectory = Settings.Default.WalletDirectory;
			try
			{
				Process lProcess = Process.Start( lPSI );
				lock( mLocks )
				{
					if( !mLocks.ContainsKey( lProcess.Id ) ) mLocks[ lProcess.Id ] = new Object( );
				}
				lock( mLocks[ lProcess.Id ] )
				{
					if( !mOutputData.ContainsKey( lProcess.Id ) ) mOutputData[ lProcess.Id ] = "";
					lProcess.OutputDataReceived += lProcess_DataReceived;
					lProcess.BeginOutputReadLine( );
					lProcess.BeginErrorReadLine( );
					mProcesses[ lsWallet ] = lProcess;
				}
			}
			catch( Exception lException )
			{
				lock( mConsoleLock )
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine( lException.ToString( ) );
					Console.ResetColor( );
				}
				mProcesses[ lsWallet ] = new Process( );
			}
		}

		private static void MonitorOutput( )
		{
			while( mProcesses.Count > 0 )
			{
				foreach( string lsWallet in masWallets )
				{
					if( !mProcesses.ContainsKey( lsWallet ) ) continue;
					Process lProcess = mProcesses[ lsWallet ];
					int lnKey = lProcess.Id;
					String lsFoundLine;
					lock( mLocks[ lnKey ] )
					{
						string lsCurrent = mOutputData[ lnKey ].Trim( );
						int lnIndex = lsCurrent.IndexOfAny( new char[ ] { '\r', '\n' } );
						if( lnIndex > 0 )
						{
							lsFoundLine = lsCurrent.Substring( 0, lnIndex ).Trim( );
							lsCurrent = lsCurrent.Substring( lnIndex ).Trim( );
							mOutputData[ lnKey ] = lsCurrent;
						}
						else
						{
							lsFoundLine = lsCurrent;
							mOutputData[ lnKey ] = "";
							if( lProcess.HasExited ) mProcesses.Remove( lsWallet );
						}
					}
					if( lsFoundLine.Length > 0 )
					{
						Console.ForegroundColor = ConsoleColor.Yellow;
						Console.Write( lsWallet );
						Console.ResetColor( );
						Console.CursorLeft = mnWalletNameLength + 4;
						Console.Write( lsFoundLine + "\n" );
						Match lRegexMatch = Regex.Match( lsFoundLine, Settings.Default.RegexBalanceMatch, RegexOptions.ExplicitCapture );
						if( lRegexMatch.Success )
						{
							mBalances[ lnKey ] = Convert.ToDecimal( lRegexMatch.Groups[ "Balance" ].Value, CultureInfo.InvariantCulture );
							mUnlockedBalances[ lnKey ] = Convert.ToDecimal( lRegexMatch.Groups[ "UnlockedBalance" ].Value, CultureInfo.InvariantCulture );
							lock( Settings.Default )
							{
								if( Settings.Default.AutoTransferAddress.Length > 0 && mUnlockedBalances[ lnKey ] > Settings.Default.MinimumAutoTransfer + Settings.Default.Fee )
								{
									decimal lToTransfer = mUnlockedBalances[ lnKey ] - Settings.Default.Fee;
									lProcess.StandardInput.WriteLine( ( "transfer " + Settings.Default.MixInCount + " " + Settings.Default.AutoTransferAddress + " " + lToTransfer.ToString( CultureInfo.InvariantCulture ) + " " + Settings.Default.AutoTransferPaymentID ).TrimEnd( ) );
									Settings.Default.TransferredSoFar += lToTransfer;
									Settings.Default.Save( );
									Thread.Sleep( 5000 );
									lock( mConsoleLock )
									{
										lProcess.StandardInput.WriteLine( "refresh" );
										lProcess.StandardInput.WriteLine( "save" );
										lProcess.StandardInput.WriteLine( "balance" );
									}
								}
							}
							Decimal lTotalBalance = mBalances.Values.Sum( );
							Decimal lTotalUnlockedBalance = mUnlockedBalances.Values.Sum( );

							lock( mConsoleLock )
							{
								Console.ForegroundColor = ConsoleColor.Green;
								Console.WriteLine( "Total balance:                        " + lTotalBalance.ToString( CultureInfo.CurrentCulture ) );
								Console.WriteLine( "Total unlocked balance:               " + lTotalUnlockedBalance.ToString( CultureInfo.CurrentCulture ) );
								Console.WriteLine( "Total transferred:                    " + Settings.Default.TransferredSoFar.ToString( CultureInfo.CurrentCulture ) );
								Console.WriteLine( "Total balance, including transferred: " + ( lTotalBalance + Settings.Default.TransferredSoFar ).ToString( CultureInfo.CurrentCulture ) );
								Console.ResetColor( );
							}
						}
					}
					Thread.Sleep( 0 );
				}
			}
		}

		private static void lProcess_DataReceived( object lSender, DataReceivedEventArgs lEventArgs )
		{
			Process lProcess = ( Process ) lSender;
			lock( mLocks[ lProcess.Id ] )
			{
				mOutputData[ lProcess.Id ] = mOutputData[ lProcess.Id ] + lEventArgs.Data + "\n";
			}
		}
	}
}