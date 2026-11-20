using Com0com.Core;

namespace Com0com.Cli;

internal static class Help
{
    public const string Main = """
        setupcn - NewVirtualCOMPair command line (virtual serial port pairs, com0com driver)

        Usage:
          setupcn [options] <command> [arguments]
          setupcn                      start an interactive shell

        Port pairs:
          list                                  list installed pairs and their parameters
          pair add [<n>] [-a <prms>] [-b <prms>] [--both <prms>]
                                                install a pair CNCA<n>/CNCB<n> (default <n>: first free)
          pair remove <n> [<n>...]              remove pairs
          port show <CNCxN>                     show all parameters of a port
          port set <CNCxN> <Key=Value>...       change port parameters
          port reset <CNCxN> [<Key>...]         reset parameters (all if none given) to defaults
          enable | disable                      enable / disable all com0com devices

          Shortcuts: add = pair add, rm = pair remove, ls = list, set = port set, show = port show

        Driver:
          driver install                        install the driver for pairs added with --no-update
          driver update                         force-update the driver from the INF directory
          driver reload                         restart all com0com devices (reload the driver)
          driver preinstall                     install device class and INF files only
          driver uninstall                      remove all pairs, the service and the driver
          driver clean                          remove stale com0com INF files from %windir%\INF

        Names:
          names busy [<pattern>]                names already in use (wildcards * and ?)
          names friendly [--update]             show (and update) device friendly names

        Other:
          wait [[+]<sec>]                       wait for pending device installs (default: forever)
          version                               show version
          help [<topic>]                        help; topics: params, examples, options

        Options:
          -s, --silent           never prompt; take default answers (for scripts)
          -q, --quiet            only print results, not progress
          -w, --wait [+]<sec>    after the command wait for PnP to finish; '+' asks to keep waiting
          --inf-dir <dir>        directory with com0com.inf/.sys/.cat (default: program directory)
          --detail               show default values too
          --json                 machine readable output for list / port show / names busy / pair add
          --no-update            pair add: don't install the driver yet (run 'driver install' later)
          --no-fnames            don't update device friendly names
          --show-fnames          trace friendly name changes
          -o, --output <file>    append the output to <file>

        Exit codes: 0 ok, 1 failed or cancelled, 2 usage error, 3010 ok but reboot required.

        """;

    public const string Examples = """
        Examples:
          setupcn add -a COM5 -b COM6                 pair COM5 <-> COM6
          setupcn add 3                               pair CNCA3 <-> CNCB3 with default names
          setupcn add -a PortName=COM# -b COM#        let Windows assign COM numbers (Ports class)
          setupcn add -a COM7,EmuBR=yes --both EmuOverrun=yes
          setupcn set CNCA0 PortName=COM10 EmuBR=yes
          setupcn set CNCA0 RealPortName=COM12        rename a PortName=COM# port
          setupcn set CNCB0 cts=ropen dsr=!rdtr
          setupcn reset CNCA0 EmuBR                   EmuBR back to the driver default
          setupcn list --json
          setupcn rm 0
          setupcn -s --wait 30 add -a COM20 -b COM21  unattended install
          setupcn names busy COM?*

        Legacy setupc command mapping:
          install - -                    ->  add
          install 5 * *                  ->  add 5
          install PortName=COM2 PortName=COM4  ->  add -a COM2 -b COM4
          change CNCA0 EmuBR=yes         ->  set CNCA0 EmuBR=yes
          remove 0                       ->  rm 0
          list                           ->  list
          update / reload / uninstall    ->  driver update / driver reload / driver uninstall
          preinstall / infclean          ->  driver preinstall / driver clean
          install (no args)              ->  driver install
          busynames COM?*                ->  names busy COM?*
          listfnames / updatefnames      ->  names friendly / names friendly --update
          disable all / enable all       ->  disable / enable

        """;

    public static string For(string topic) => topic.ToLowerInvariant() switch
    {
        "params" or "parameters" or "port" or "set" => PortParameters.Help + "\n",
        "examples" or "example" or "legacy" => Examples,
        _ => Main,
    };
}
