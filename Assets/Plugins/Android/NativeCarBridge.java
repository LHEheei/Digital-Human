package com.srtp.avatar;

import android.Manifest;
import android.app.Activity;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothSocket;
import android.content.pm.PackageManager;
import android.os.Build;

import com.unity3d.player.UnityPlayer;

import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.Set;
import java.util.UUID;

public final class NativeCarBridge {
    private static final UUID SPP_UUID = UUID.fromString("00001101-0000-1000-8000-00805F9B34FB");
    private final Activity activity;
    private final String unityObjectName;
    private volatile BluetoothSocket activeSocket;

    public NativeCarBridge(Activity activity, String unityObjectName) {
        this.activity = activity;
        this.unityObjectName = unityObjectName;
    }

    public void sendCommand(final String deviceName, final String deviceAddress, final String command, final String commandId) {
        new Thread(new Runnable() {
            @Override
            public void run() {
                BluetoothSocket socket = null;
                try {
                    if (activity == null) {
                        throw new IllegalStateException("Android Activity 不可用。");
                    }

                    if (!hasBluetoothConnectPermission()) {
                        requestBluetoothConnectPermission();
                        throw new IllegalStateException("缺少蓝牙连接权限，请授权后重试。");
                    }

                    BluetoothAdapter adapter = BluetoothAdapter.getDefaultAdapter();
                    if (adapter == null) {
                        throw new IllegalStateException("当前设备不支持蓝牙。");
                    }

                    if (!adapter.isEnabled()) {
                        throw new IllegalStateException("蓝牙未开启。");
                    }

                    BluetoothDevice device = resolveDevice(adapter, deviceName, deviceAddress);
                    if (device == null) {
                        throw new IllegalStateException("未找到已配对的小车蓝牙设备。");
                    }

                    adapter.cancelDiscovery();
                    socket = device.createRfcommSocketToServiceRecord(SPP_UUID);
                    activeSocket = socket;
                    socket.connect();

                    OutputStream output = socket.getOutputStream();
                    output.write((command == null ? "" : command).getBytes(StandardCharsets.UTF_8));
                    output.flush();
                    send(commandId, "OnAndroidCarCommandSent", "ok");
                } catch (Exception ex) {
                    send(commandId, "OnAndroidCarCommandError", ex.getMessage());
                } finally {
                    if (socket != null) {
                        try {
                            socket.close();
                        } catch (Exception ignored) {
                        }
                        if (activeSocket == socket) {
                            activeSocket = null;
                        }
                    }
                }
            }
        }).start();
    }

    public void cancelActiveCommand() {
        BluetoothSocket socket = activeSocket;
        activeSocket = null;
        if (socket == null) {
            return;
        }

        try {
            socket.close();
        } catch (Exception ignored) {
        }
    }

    private BluetoothDevice resolveDevice(BluetoothAdapter adapter, String deviceName, String deviceAddress) {
        if (deviceAddress != null && deviceAddress.trim().length() > 0) {
            return adapter.getRemoteDevice(deviceAddress.trim());
        }

        String expectedName = deviceName == null ? "" : deviceName.trim();
        Set<BluetoothDevice> bondedDevices = adapter.getBondedDevices();
        if (bondedDevices == null) {
            return null;
        }

        for (BluetoothDevice device : bondedDevices) {
            String bondedName = device.getName();
            if (expectedName.length() == 0 || (bondedName != null && bondedName.equalsIgnoreCase(expectedName))) {
                return device;
            }
        }

        for (BluetoothDevice device : bondedDevices) {
            String bondedName = device.getName();
            if (bondedName != null && bondedName.toLowerCase().contains(expectedName.toLowerCase())) {
                return device;
            }
        }

        return null;
    }

    private boolean hasBluetoothConnectPermission() {
        return Build.VERSION.SDK_INT < Build.VERSION_CODES.S
            || activity.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED;
    }

    private void requestBluetoothConnectPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            activity.runOnUiThread(new Runnable() {
                @Override
                public void run() {
                    activity.requestPermissions(new String[] { Manifest.permission.BLUETOOTH_CONNECT }, 6205);
                }
            });
        }
    }

    private void send(String commandId, String method, String message) {
        UnityPlayer.UnitySendMessage(unityObjectName, method, (commandId == null ? "" : commandId) + "\n" + (message == null ? "" : message));
    }
}
