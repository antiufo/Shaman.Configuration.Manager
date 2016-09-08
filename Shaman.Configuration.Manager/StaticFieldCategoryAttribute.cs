﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shaman.Runtime
{
#if !STANDALONE
    [RestrictedAccess]
#endif
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Property)]
#if STANDALONE
    internal
#else
    public
#endif
    class StaticFieldCategoryAttribute : Attribute
    {
        public StaticFieldCategoryAttribute(StaticFieldCategory category) { }
    }

#if STANDALONE
    internal
#else
    [RestrictedAccess]
    public
#endif
        enum StaticFieldCategory
    {
        Unknown,
        Stable,
        ObjectManager,
        Configuration,
        TODO,
        Querylang,
        TryFinally,
        TrimmedCache,
        Cache
    }
}
