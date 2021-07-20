using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vim.EditorHost.Implementation.Misc
{
    [Export(typeof(IObscuringTipManager))]
    internal sealed class BasicObscuringTipManager : IObscuringTipManager
    {
        void IObscuringTipManager.PushTip(ITextView view, IObscuringTip tip)
        {
        }

        void IObscuringTipManager.RemoveTip(ITextView view, IObscuringTip tip)
        {
        }
    }
}
