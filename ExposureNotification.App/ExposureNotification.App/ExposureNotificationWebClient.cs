﻿using ExposureNotification.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Essentials;

namespace ExposureNotification.App
{
	public class ExposureNotificationWebClient
	{
		static readonly HttpClient http = new HttpClient();

		public ExposureNotificationWebClient(string webServiceUrlBase, ITemporaryExposureKeyEncoding encoder)
		{
			WebServiceUrlBase = webServiceUrlBase;
			Encoder = encoder;
		}

		public string WebServiceUrlBase { get; }

		public ITemporaryExposureKeyEncoding Encoder { get; }

		public async Task<List<TemporaryExposureKey>> GetKeysAsync()
		{
			const string prefsSinceKey = "keys_since";

			// Get the newest date we have keys from and request since then
			// or if no date stored, only return as much as the past 14 days of keys
			var since = Preferences.Get(prefsSinceKey, DateTime.UtcNow.AddDays(-14));

			var url = GetUrl($"keys?since={since:o}");

			var response = await http.GetAsync(url);

			response.EnsureSuccessStatusCode();

			var json = await response.Content.ReadAsStringAsync();

			var keys = JsonConvert.DeserializeObject<KeysResponse>(json);

			// Save newest timestamp for next request
			Preferences.Set(prefsSinceKey, keys.Timestamp.ToUniversalTime());

			return keys.Keys;
		}

		const string prefsDiagnosisUidKey = "diagnosis_uid";

		public Task SubmitPositiveDiagnosisAsync(IEnumerable<Xamarin.ExposureNotifications.TemporaryExposureKey> keys)
			=> SubmitPositiveDiagnosisAsync(Preferences.Get(prefsDiagnosisUidKey, (string)default), keys);

		public async Task SubmitPositiveDiagnosisAsync(string diagnosisUid, IEnumerable<Xamarin.ExposureNotifications.TemporaryExposureKey> keys)
		{
			if (string.IsNullOrEmpty(diagnosisUid))
				throw new InvalidOperationException();

			var url = GetUrl($"diagnosis");

			var encodedKeys = keys.Select(k => new TemporaryExposureKey
			{
				KeyData = Encoder.Encode(k.KeyData),
				RollingStart = k.RollingStart,
				RollingDuration = (int)k.RollingDuration.TotalMinutes,
				TransmissionRiskLevel = (int)k.TransmissionRiskLevel
			});

			var json = JsonConvert.SerializeObject(new DiagnosisSubmission
			{
				DiagnosisUid = diagnosisUid,
				TemporaryExposureKeys = encodedKeys.ToList()
			});

			var response = await http.PostAsync(url, new StringContent(json));

			response.EnsureSuccessStatusCode();

			Preferences.Set(prefsDiagnosisUidKey, diagnosisUid);
		}

		string GetUrl(string path)
			=> WebServiceUrlBase.TrimEnd('/') + path;
	}
}
