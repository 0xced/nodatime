﻿#region Copyright and license information

// Copyright 2001-2009 Stephen Colebourne
// Copyright 2009 Jon Skeet
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using System;

using NodaTime.Base;

namespace NodaTime
{
    /// <summary>
    /// Original name: Instant.
    /// </summary>
    public sealed class Instant : AbstractInstant
    {
        /// <summary>
        /// This takes the place of the old parameterless constructor; it's more inkeeping
        /// with DateTime.Now etc, whereas java.util.Date/Calendar support the parameterless
        /// constructor style. Let's get rid of that ambiguity.
        /// </summary>
        public static Instant Now
        {
            get { throw new NotImplementedException(); }
        }

        public Instant(long instant)
        {
            throw new NotImplementedException();
        }

        public Instant(object instant)
        {
            throw new NotImplementedException();
        }

        public Instant WithMilliseconds(long milliseconds)
        {
            throw new NotImplementedException();
        }

        public Instant WithDurationAdded(long durationToAdd, int scalar)
        {
            throw new NotImplementedException();
        }

        public Instant WithDurationAdded(Duration durationToAdd, int scalar)
        {
            throw new NotImplementedException();
        }

        public Instant Plus(IDuration duration)
        {
            throw new NotImplementedException();
        }

        public Instant Minus(IDuration duration)
        {
            throw new NotImplementedException();
        }

        public Instant Plus(long duration)
        {
            throw new NotImplementedException();
        }

        public Instant Minus(long duration)
        {
            throw new NotImplementedException();
        }
    }
}