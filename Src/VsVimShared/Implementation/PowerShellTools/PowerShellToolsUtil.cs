using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Composition;

namespace Vim.VisualStudio.Implementation.PowerShellTools
{
    [Export(typeof(IPowerShellToolsUtil))]
    internal sealed class PowerShellToolsUtil : IPowerShellToolsUtil
    {
        //https://github.com/adamdriscoll/poshtools/blob/dev/PowerShellTools/Guids.cs
        //Visual Studio 2017,2019,2022
        private static readonly Guid s_powerShellToolsPackageIdDev15 = new Guid("{0429083f-fdbc-47a3-84ff-b3d50343b21e}");
        private readonly bool _isPowerShellToolsInstalled;

        [ImportingConstructor]
        internal PowerShellToolsUtil(SVsServiceProvider serviceProvider)
        {
            var dte = serviceProvider.GetService<SDTE, _DTE>();
            var guid = s_powerShellToolsPackageIdDev15;
            var vsShell = serviceProvider.GetService<SVsShell, IVsShell>();

            _isPowerShellToolsInstalled = vsShell.IsPackageInstalled(guid);
        }

        #region IPowerShellToolsUtil

        bool IPowerShellToolsUtil.IsInstalled
        {
            get { return _isPowerShellToolsInstalled; }
        }

        #endregion

    }
}
