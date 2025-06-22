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

using XactJobs.EntityConfigurations;

namespace Microsoft.EntityFrameworkCore
{
    public static class ModelBuilderExtensions
    {
        public static void ApplyXactJobsConfigurations(this ModelBuilder modelBuilder, string? providerName, bool excludeFromMigrations = true)
        {
            modelBuilder.ApplyConfiguration(new XactJobEntityConfiguration(providerName, excludeFromMigrations));
            modelBuilder.ApplyConfiguration(new XactJobHistoryEntityConfiguration(providerName, excludeFromMigrations));
            modelBuilder.ApplyConfiguration(new XactJobPeriodicEntityConfiguration(providerName, excludeFromMigrations));
        }
    }
}
