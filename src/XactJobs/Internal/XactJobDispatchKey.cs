// This file is part of XactJobs.
//
// XactJobs is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// XactJobs is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

namespace XactJobs.Internal
{
    internal readonly struct XactJobDispatchKey : IEquatable<XactJobDispatchKey>
    {
        public string TypeName { get; }
        public string MethodName { get; }
        public int ArgCount { get; }

        public XactJobDispatchKey(string typeName, string methodName, int argCount)
        {
            TypeName = typeName;
            MethodName = methodName;
            ArgCount = argCount;
        }

        public bool Equals(XactJobDispatchKey other) =>
            TypeName == other.TypeName && MethodName == other.MethodName && ArgCount == other.ArgCount;

        public override bool Equals(object? obj) => obj is XactJobDispatchKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(TypeName, MethodName, ArgCount);
    }

}
