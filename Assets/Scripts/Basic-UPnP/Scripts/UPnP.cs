using System;
using Open.Nat;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Net;

/// <summary>
/// Static helper class for UPnP
/// </summary>
public static class UPnP
{
	/// <summary>
	/// Try to get External IP (provided by FAI), pass this IP to Client
	/// </summary>
	/// <returns>Async Task, IPAddress</returns>
	public static async Task<IPAddress> GetExternalIpAsync()
	{
		NatDevice router = await GetInterDevice();

		if (router == null)
		{
			throw new NullReferenceException();
		}
		
		return await router.GetExternalIPAsync();
	}

	/// <summary>
	/// Try to create a UPnP rule on the router
	/// </summary>
	/// <param name="port">Port to redirect to the host device</param>
	/// <param name="ruleName">Rule name displayed on Router interface</param>
	/// <returns>Async Task, Bool = success</returns>
	public static async Task<bool> RedirectExternalConnectionToThisDevice(int port, string ruleName)
	{
		try
		{
			NatDevice router = await GetInterDevice();

			if (router == null)
			{
				return false;
			}

			Mapping specificMapping = await router.GetSpecificMappingAsync(Protocol.Udp, port);
			if (specificMapping == null)
			{
				await router.CreatePortMapAsync(new Mapping(Protocol.Udp, port, port, ruleName));
				Logger.Log($"Router rule created successfully for port {port}");
			}
		}
        catch (MappingException e)
        {
	        Logger.Log(e.Message, Logger.LogLevel.Error);
            return false;
        }

		return true;
	}

	/// <summary>
	/// try to retrieve a UPnP compatible Device on the Route
	/// </summary>
	/// <returns>Async Task, NatDevice</returns>
	private static async Task<NatDevice> GetInterDevice()
	{
		var timeoutMs = 500;
		const int timeoutMaxMs = 15000;
		const int discoveryIntervalMs = 500;
		
		NatDiscoverer discoverer = new();
		List<NatDevice> devices;

		while (true)
		{
			try
			{
				CancellationTokenSource cts = new(timeoutMs);
				devices = new List<NatDevice>(await discoverer.DiscoverDevicesAsync(PortMapper.Upnp, cts));

				if (devices.Count > 0)
				{
					break;
				}
			}
			catch (TaskCanceledException)
			{
				Logger.Log("Can`t find UPnP device. Trying again with double timeout...");
			}
			
			timeoutMs *= 2;
			if (timeoutMs >= timeoutMaxMs)
			{
				return null;
			}

			await Task.Delay(TimeSpan.FromMilliseconds(discoveryIntervalMs));
		}
		
		foreach (NatDevice device in devices)
		{
			if (device.LocalAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
			{
				return device;
			}
		}

		return null;
	}

	public static async void DeleteRuleAsync(ushort privatePort)
	{
		IPAddress localIpAddress = await ConnectionDataPresenter.GetLocalIpAddressAsync();
		IPEndPoint endPoint = new(localIpAddress, privatePort);
		List<Mapping> matchingMappings = (await GetMatchingMappingsAsync(endPoint)).ToList();
		
		if (matchingMappings.Any() == false)
		{
			Logger.Log($"No router rule found for port {privatePort}", Logger.LogLevel.Error);
			return;
		}

		NatDevice router = await GetInterDevice();
		foreach (Mapping mapping in matchingMappings)
		{
			await router.DeletePortMapAsync(mapping);
			Logger.Log($"Router rule (port: {privatePort}) deleted successfully");
		}
	}
	
	public static async Task<IEnumerable<Mapping>> GetMatchingMappingsAsync(IPEndPoint ipEndPoint)
	{
		NatDevice router = await GetInterDevice();
		IEnumerable<Mapping> allMappings = await router.GetAllMappingsAsync();
		List<Mapping> matchingMappings = allMappings.Where(x => x.PrivatePort == ipEndPoint.Port && Equals(x.PrivateIP, ipEndPoint.Address)).ToList();
		
		return matchingMappings;
	}
}