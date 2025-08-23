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

namespace XactJobs
{
    public class XactJob: XactJobBase
    {
        public DateTime? LeasedUntil { get; init; }
        public Guid? Leaser { get; init; }

        public XactJob(long id,
                       DateTime scheduledAt,
                       string typeName,
                       string methodName,
                       string methodArgs,
                       string queue,
                       string? periodicJobId = null,
                       string? cronExpression = null,
                       int? periodicJobVersion = null,
                       int errorCount = 0,
                       Guid? leaser = null,
                       DateTime? leasedUntil = null)
            : base(id,
                   scheduledAt,
                   typeName,
                   methodName,
                   methodArgs,
                   queue,
                   periodicJobId,
                   cronExpression,
                   periodicJobVersion,
                   errorCount)
        {
            Leaser = leaser;
            LeasedUntil = leasedUntil;
        }
    }
} 