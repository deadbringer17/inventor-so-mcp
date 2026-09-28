package com.occhipinti.inventorxrso;

import android.content.Context;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraManager;

/** Resolve the Camera2 index used by Unity WebCamTexture, excluding avatar/spatial cameras. */
public final class PassthroughCameraSelector {
    private PassthroughCameraSelector() { }

    public static int findIndex(Context context) throws Exception {
        CameraManager manager = (CameraManager) context.getSystemService(Context.CAMERA_SERVICE);
        String[] ids = manager.getCameraIdList();
        CameraCharacteristics.Key<Byte> sourceKey = new CameraCharacteristics.Key<>(
                "com.meta.extra_metadata.camera_source", Byte.class);
        CameraCharacteristics.Key<Byte> positionKey = new CameraCharacteristics.Key<>(
                "com.meta.extra_metadata.position", Byte.class);
        int fallback = -1;
        for (int i = 0; i < ids.length; i++) {
            CameraCharacteristics characteristics = manager.getCameraCharacteristics(ids[i]);
            Byte source;
            try { source = characteristics.get(sourceKey); }
            catch (IllegalArgumentException ignored) { continue; }
            if (source == null || source != 0) continue;
            fallback = i;
            Byte position;
            try { position = characteristics.get(positionKey); }
            catch (IllegalArgumentException ignored) { continue; }
            if (position != null && position == 0) return i;
        }
        return fallback;
    }
}
