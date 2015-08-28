//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.18449
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using UnityEngine;
namespace BahaTurret
{
	public static class RadarUtils
	{

		public static RenderTexture radarRT;
		public static Texture2D radarTex2D;
		public static Camera radarCam;
		public static Shader radarShader;
		public static int radarResolution = 32;



		public static void SetupRadarCamera()
		{
			if(radarRT && radarTex2D && radarCam && radarShader)
			{
				return;
			}

			//setup shader first
			if(!radarShader)
			{
				radarShader = BDAShaderLoader.LoadManifestShader("BahaTurret.UnlitBlack.shader");
			}

			//then setup textures
			radarRT = new RenderTexture(radarResolution,radarResolution,16);
			radarTex2D = new Texture2D(radarResolution,radarResolution, TextureFormat.ARGB32, false);

			//set up camera
			radarCam = (new GameObject("RadarCamera")).AddComponent<Camera>();
			radarCam.enabled = false;
			radarCam.clearFlags = CameraClearFlags.SolidColor;
			radarCam.backgroundColor = Color.white;
			radarCam.SetReplacementShader(radarShader, string.Empty);
			radarCam.cullingMask = 1<<0;
			radarCam.targetTexture = radarRT;
			//radarCam.nearClipPlane = 75;
			//radarCam.farClipPlane = 40000;
		}

		public static float GetRadarSnapshot(Vessel v, Vector3 origin, float camFoV)
		{
			if(v.Landed)
			{
				return 0;
			}
			TargetInfo ti = v.GetComponent<TargetInfo>();
			if(ti && ti.isMissile)
			{
				return 600;
			}

			float distance = (v.transform.position - origin).magnitude;

			radarCam.nearClipPlane = Mathf.Clamp(distance - 200, 20, 40000);
			radarCam.farClipPlane = Mathf.Clamp(distance + 200, 20, 40000);

			radarCam.fieldOfView = camFoV;

			radarCam.transform.position = origin;
			radarCam.transform.LookAt(v.CoM+(v.rb_velocity*Time.fixedDeltaTime));

			float pixels = 0;
			RenderTexture.active = radarRT;

			radarCam.Render();
			
			radarTex2D.ReadPixels(new Rect(0,0,radarResolution,radarResolution), 0,0);

			for(int x = 0; x < radarResolution; x++)
			{
				for(int y = 0; y < radarResolution; y++)	
				{
					if(radarTex2D.GetPixel(x,y).r<1)
					{
						pixels++;	
					}
				}
			}


			return pixels*4;
		}


		public static void ScanInDirection(MissileFire myWpnManager, float directionAngle, Transform referenceTransform, float fov, Vector3 position, float minSignature, ref TargetSignatureData[] dataArray, float dataPersistTime, bool pingRWR, RadarWarningReceiver.RWRThreatTypes rwrType, bool radarSnapshot)
		{
	

			Vector3d geoPos = VectorUtils.WorldPositionToGeoCoords(position, FlightGlobals.currentMainBody);
			Vector3 forwardVector = referenceTransform.forward;
			Vector3 upVector = referenceTransform.up;//VectorUtils.GetUpDirection(position);
			Vector3 lookDirection = Quaternion.AngleAxis(directionAngle, upVector) * forwardVector;

			int dataIndex = 0;
			foreach(var vessel in BDATargetManager.LoadedVessels)
			{
				if(vessel == null) continue;
				if(!vessel.loaded) continue;

				if(myWpnManager)
				{
					if(vessel == myWpnManager.vessel) continue; //ignore self
				}
				else if((vessel.transform.position - position).sqrMagnitude < 3600) continue;

				Vector3 vesselDirection = Vector3.ProjectOnPlane(vessel.CoM - position, upVector);

				if(Vector3.Angle(vesselDirection, lookDirection) < fov / 2)
				{
					if(TerrainCheck(referenceTransform.position, vessel.transform.position)) continue; //blocked by terrain

					float sig = float.MaxValue;
					if(radarSnapshot && minSignature > 0) sig = GetModifiedSignature(vessel, position);

					RadarWarningReceiver.PingRWR(vessel, position, rwrType, dataPersistTime);

					float detectSig = sig;

					VesselECMJInfo vesselJammer = vessel.GetComponent<VesselECMJInfo>();
					if(vesselJammer)
					{
						sig *= vesselJammer.rcsReductionFactor;
						detectSig += vesselJammer.jammerStrength;
					}

					if(detectSig > minSignature)
					{
						if(vessel.vesselType == VesselType.Debris)
						{
							vessel.gameObject.AddComponent<TargetInfo>();
						}
						else if(myWpnManager != null)
						{
							BDATargetManager.ReportVessel(vessel, myWpnManager);
						}

						while(dataIndex < dataArray.Length - 1)
						{
							if((dataArray[dataIndex].exists && Time.time - dataArray[dataIndex].timeAcquired > dataPersistTime) || !dataArray[dataIndex].exists)
							{
								break;
							}
							dataIndex++;
						}
						if(dataIndex >= dataArray.Length) break;
						dataArray[dataIndex] = new TargetSignatureData(vessel, sig);
						dataIndex++;
						if(dataIndex >= dataArray.Length) break;
					}
				}
			}

		}

		public static void ScanInDirection(Ray ray, float fov, float minSignature, ref TargetSignatureData[] dataArray, float dataPersistTime, bool pingRWR, RadarWarningReceiver.RWRThreatTypes rwrType, bool radarSnapshot)
		{
			int dataIndex = 0;
			foreach(var vessel in BDATargetManager.LoadedVessels)
			{
				if(vessel == null) continue;
				if(!vessel.loaded) continue;
				if(vessel.Landed) continue;

				Vector3 vectorToTarget = vessel.transform.position - ray.origin;
				if((vectorToTarget).sqrMagnitude < 10) continue; //ignore self

				if(Vector3.Dot(vectorToTarget, ray.direction) < 0) continue; //ignore behind ray

				if(Vector3.Angle(vessel.CoM - ray.origin, ray.direction) < fov / 2)
				{
					if(TerrainCheck(ray.origin, vessel.transform.position)) continue; //blocked by terrain
					float sig = float.MaxValue;
					if(radarSnapshot) sig = GetModifiedSignature(vessel, ray.origin);

					if(pingRWR && sig > minSignature * 0.66f)
					{
						RadarWarningReceiver.PingRWR(vessel, ray.origin, rwrType, dataPersistTime);
					}

					if(sig > minSignature)
					{
						while(dataIndex < dataArray.Length - 1)
						{
							if((dataArray[dataIndex].exists && Time.time - dataArray[dataIndex].timeAcquired > dataPersistTime) || !dataArray[dataIndex].exists)
							{
								break;
							}
							dataIndex++;
						}
						if(dataIndex >= dataArray.Length) break;
						dataArray[dataIndex] = new TargetSignatureData(vessel, sig);
						dataIndex++;
						if(dataIndex >= dataArray.Length) break;
					}
				}

			}
		}

		/// <summary>
		/// Scans for targets in direction with field of view.
		/// Returns the direction scanned for debug 
		/// </summary>
		/// <returns>The scan direction.</returns>
		/// <param name="myWpnManager">My wpn manager.</param>
		/// <param name="directionAngle">Direction angle.</param>
		/// <param name="referenceTransform">Reference transform.</param>
		/// <param name="fov">Fov.</param>
		/// <param name="results">Results.</param>
		/// <param name="maxDistance">Max distance.</param>
		public static Vector3 GuardScanInDirection(MissileFire myWpnManager, float directionAngle, Transform referenceTransform, float fov, out ViewScanResults results, float maxDistance)
		{
			results = new ViewScanResults();
			results.foundHeatMissile = false;
			results.foundAGM = false;
			results.firingAtMe = false;

			if(!myWpnManager || !referenceTransform)
			{
				return Vector3.zero;
			}

			Vector3 position = referenceTransform.position;
			Vector3d geoPos = VectorUtils.WorldPositionToGeoCoords(position, FlightGlobals.currentMainBody);
			Vector3 forwardVector = referenceTransform.forward;
			Vector3 upVector = referenceTransform.up;
			Vector3 lookDirection = Quaternion.AngleAxis(directionAngle, upVector) * forwardVector;



			foreach(var vessel in BDATargetManager.LoadedVessels)
			{
				if(vessel == null) continue;

				if(vessel.loaded)
				{
					if(vessel == myWpnManager.vessel) continue; //ignore self

					Vector3 vesselProjectedDirection = Vector3.ProjectOnPlane(vessel.transform.position-position, upVector);
					Vector3 vesselDirection = vessel.transform.position - position;


					if(Vector3.Dot(vesselDirection, lookDirection) < 0) continue;

					float vesselDistance = (vessel.transform.position - position).magnitude;

					if(vesselDistance < maxDistance && Vector3.Angle(vesselProjectedDirection, lookDirection) < fov / 2 && Vector3.Angle(vessel.transform.position-position, -myWpnManager.transform.forward) < myWpnManager.guardAngle/2)
					{
						//Debug.Log("Found vessel: " + vessel.vesselName);
						if(TerrainCheck(referenceTransform.position, vessel.transform.position)) continue; //blocked by terrain

						BDATargetManager.ReportVessel(vessel, myWpnManager);

						TargetInfo tInfo;
						if((tInfo = vessel.GetComponent<TargetInfo>()))
						{
							if(tInfo.isMissile)
							{
								MissileLauncher missile;
								if(missile = tInfo.missileModule)
								{
									if(missile.hasFired && (missile.targetPosition - (myWpnManager.vessel.CoM + (myWpnManager.vessel.rb_velocity * Time.fixedDeltaTime))).sqrMagnitude < 3600)
									{
										//Debug.Log("found missile targeting me");
										if(missile.targetingMode == MissileLauncher.TargetingModes.Heat)
										{
											results.foundHeatMissile = true;
											break;
										}
										else if(missile.targetingMode == MissileLauncher.TargetingModes.Laser)
										{
											results.foundAGM = true;
											break;
										}
									}
									else
									{
										break;
									}
								}
							}
							else
							{
								//check if its shooting guns at me
								if(!results.firingAtMe)
								{
									foreach(var weapon in vessel.FindPartModulesImplementing<ModuleWeapon>())
									{
										if(!weapon.isFiring) continue;
										if(Vector3.Dot(weapon.fireTransforms[0].forward, vesselDirection) > 0) continue;

										if(Vector3.Angle(weapon.fireTransforms[0].forward, -vesselDirection) < 6500 / vesselDistance)
										{
											results.firingAtMe = true;
										}
									}
								}
							}
						}
					}
				}
			}

			return lookDirection;
		}

		public static float GetModifiedSignature(Vessel vessel, Vector3 origin)
		{
			//float sig = GetBaseRadarSignature(vessel);
			float sig = GetRadarSnapshot(vessel, origin, 0.1f);

			Vector3 upVector = VectorUtils.GetUpDirection(origin);
			
			//sig *= Mathf.Pow(15000,2)/(vessel.transform.position-origin).sqrMagnitude;
			
			if(vessel.Landed)
			{
				sig /= 550;
			}
			if(vessel.Splashed)
			{
				sig /= 250;
			}
			
			//notching and ground clutter
			Vector3 targetDirection = (vessel.transform.position-origin).normalized;
			Vector3 targetClosureV = Vector3.ProjectOnPlane(Vector3.Project(vessel.srf_velocity,targetDirection), upVector);
			float notchFactor = 1;
			float angleFromUp = Vector3.Angle(targetDirection,upVector);
			float lookDownAngle = angleFromUp-90;
			if(lookDownAngle > 5) notchFactor = Mathf.Clamp(targetClosureV.sqrMagnitude/Mathf.Pow(60,2), 0.1f, 1f);
			float groundClutterFactor = Mathf.Clamp((90/angleFromUp), 0.25f, 1.85f);
			sig *= groundClutterFactor;
			sig *= notchFactor;

			var vci = vessel.GetComponent<VesselChaffInfo>();
			if(vci) sig *= vci.GetChaffMultiplier();


			/*
			if(Mathf.Round(Time.time)%2 == 0)
			{
				Debug.Log ("================================");
				Debug.Log ("targetAxV: "+targetClosureV.magnitude);
				Debug.Log ("lookdownAngle: "+lookDownAngle);
				Debug.Log ("notchFactor: "+notchFactor);
				Debug.Log ("groundClutterFactor: "+groundClutterFactor);
			}
			*/

			return sig;
		}

		public static bool TerrainCheck(Vector3 start, Vector3 end)
		{
			return Physics.Linecast(start, end, 1<<15);
		}

	



		public static Vector2 WorldToRadar(Vector3 worldPosition, Transform referenceTransform, Rect radarRect, float maxDistance)
		{
			float scale = maxDistance/(radarRect.height/2);
			Vector3 localPosition = referenceTransform.InverseTransformPoint(worldPosition);
			localPosition.y = 0;
			Vector2 radarPos = new Vector2((radarRect.width/2)+(localPosition.x/scale), (radarRect.height/2)-(localPosition.z/scale));
			return radarPos;
		}
		
		public static Vector2 WorldToRadarRadial(Vector3 worldPosition, Transform referenceTransform, Rect radarRect, float maxDistance, float maxAngle)
		{
			float scale = maxDistance/(radarRect.height);
			Vector3 localPosition = referenceTransform.InverseTransformPoint(worldPosition);
			localPosition.y = 0;
			float angle = Vector3.Angle(localPosition, Vector3.forward);
			if(localPosition.x < 0) angle = -angle;
			float xPos = (radarRect.width/2) + ((angle/maxAngle)*radarRect.width/2);
			float yPos = radarRect.height - (new Vector2 (localPosition.x, localPosition.z)).magnitude / scale;
			Vector2 radarPos = new Vector2(xPos, yPos);
			return radarPos;
		}


	


	}
}

