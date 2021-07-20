using System;
using System.Collections.Generic;
using System.Text;

namespace Vim.EditorHost
{
    public static class EditorSpecificUtil
    {
#if VS_SPECIFIC_2017
        public const bool HasAsyncCompletion = false;
#elif VS_SPECIFIC_2019 || VS_SPECIFIC_2022 || VS_SPECIFIC_MAC
        public const bool HasAsyncCompletion = true;
#else
#error Unsupported configuration
#endif
        public const bool HasLegacyCompletion = !HasAsyncCompletion;
    }
}
