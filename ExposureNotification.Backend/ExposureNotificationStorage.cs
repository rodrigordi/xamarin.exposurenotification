﻿using ExposureNotification.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using System.Xml.Schema;

namespace ExposureNotification.Backend
{
	public class ExposureNotificationStorage : IExposureNotificationStorage
	{
		public ExposureNotificationStorage(ITemporaryExposureKeyEncoding tempExposureKeyEncoding,
			Action<DbContextOptionsBuilder> buildDbContextOpetions = null,
			Action<DbContext> initializeDb = null)
		{
			var dbContextOptionsBuilder = new DbContextOptionsBuilder();
			buildDbContextOpetions?.Invoke(dbContextOptionsBuilder);
			dbContextOptions = dbContextOptionsBuilder.Options;

			temporaryExposureKeyConding = tempExposureKeyEncoding;

			using (var ctx = new ExposureNotificationContext(dbContextOptions))
				initializeDb?.Invoke(ctx);
		}

		readonly DbContextOptions dbContextOptions;
		readonly ITemporaryExposureKeyEncoding temporaryExposureKeyConding;

		public async Task<KeysResponse> GetKeysAsync(DateTime? since)
		{
			using (var ctx = new ExposureNotificationContext(dbContextOptions))
			{
				var oldest = DateTime.UtcNow.AddDays(-14);

				if (!since.HasValue || since.Value.ToUniversalTime() < oldest)
					since = oldest;

				var results = await ctx.TemporaryExposureKeys.AsQueryable()
					.Where(dtk => dtk.Timestamp >= since)
					.ToListAsync().ConfigureAwait(false);

				var newestTimestamp = results.OrderByDescending(dtk => dtk.Timestamp).FirstOrDefault()?.Timestamp;

				return new KeysResponse {
					Timestamp = newestTimestamp ?? DateTime.MinValue,
					Keys = results.Select(dtk => new TemporaryExposureKey {
						KeyData = temporaryExposureKeyConding.Decode(Convert.FromBase64String(dtk.Base64KeyData)),
						RollingDuration = dtk.RollingDuration,
						RollingStart = dtk.RollingStart,
						TransmissionRiskLevel = dtk.TransmissionRiskLevel
					}).ToList()
				};
			}
		}

		public async Task AddDiagnosisUidsAsync(IEnumerable<string> diagnosisUids)
		{
			using (var ctx = new ExposureNotificationContext(dbContextOptions))
			{
				foreach (var d in diagnosisUids)
				{
					if (!(await ctx.Diagnoses.AnyAsync(r => r.DiagnosisUid == d)))
						ctx.Diagnoses.Add(new DbDiagnosis(d));
				}

				await ctx.SaveChangesAsync();
			}
		}

		public async Task RemoveDiagnosisUidsAsync(IEnumerable<string> diagnosisUids)
		{
			using (var ctx = new ExposureNotificationContext(dbContextOptions))
			{
				var toRemove = new List<DbDiagnosis>();

				foreach (var d in diagnosisUids)
				{
					var existingUid = await ctx.Diagnoses.FindAsync(d);
					if (existingUid != null)
						toRemove.Add(existingUid);
				}

				ctx.Diagnoses.RemoveRange(toRemove);
				await ctx.SaveChangesAsync();
			}
		}

		public Task<bool> CheckIfDiagnosisUidExistsAsync(string diagnosisUid)
		{
			using (var ctx = new ExposureNotificationContext(dbContextOptions))
				return Task.FromResult(ctx.Diagnoses.Any(d => d.DiagnosisUid.Equals(diagnosisUid)));
		}

		public async Task SubmitPositiveDiagnosisAsync(string diagnosisUid, IEnumerable<TemporaryExposureKey> keys)
		{
			using (var ctx = new ExposureNotificationContext(dbContextOptions))
			{
				// Ensure the database contains the diagnosis uid
				if (!ctx.Diagnoses.Any(d => d.DiagnosisUid == diagnosisUid))
					throw new InvalidOperationException();

				var dbKeys = keys.Select(k => new DbTemporaryExposureKey
						{
							Base64KeyData = Convert.ToBase64String(temporaryExposureKeyConding.Encode(k.KeyData)),
							Timestamp = DateTime.UtcNow
						}).ToList();

				foreach (var dbk in dbKeys)
					ctx.TemporaryExposureKeys.Add(dbk);

				await ctx.SaveChangesAsync();
			}
		}
	}
}
