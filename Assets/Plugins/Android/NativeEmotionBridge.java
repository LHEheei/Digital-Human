package com.srtp.avatar;

import android.app.Activity;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.util.Base64;

import com.google.android.gms.tasks.OnFailureListener;
import com.google.android.gms.tasks.OnSuccessListener;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.face.Face;
import com.google.mlkit.vision.face.FaceDetection;
import com.google.mlkit.vision.face.FaceDetector;
import com.google.mlkit.vision.face.FaceDetectorOptions;
import com.unity3d.player.UnityPlayer;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.List;

/**
 * Uses ML Kit's bundled face detector. JPEG data is decoded only in process
 * and is not written to disk or sent to a network service.
 */
public final class NativeEmotionBridge {
    private final Activity activity;
    private final String unityObjectName;
    private FaceDetector faceDetector;

    public NativeEmotionBridge(Activity activity, String unityObjectName) {
        this.activity = activity;
        this.unityObjectName = unityObjectName;
        FaceDetectorOptions options = new FaceDetectorOptions.Builder()
            .setPerformanceMode(FaceDetectorOptions.PERFORMANCE_MODE_FAST)
            .setClassificationMode(FaceDetectorOptions.CLASSIFICATION_MODE_ALL)
            .setMinFaceSize(0.15f)
            .build();
        faceDetector = FaceDetection.getClient(options);
    }

    public void analyzeFace(final String requestId, final String encodedJpeg) {
        if (activity == null || faceDetector == null) {
            send(requestId, "OnNativeFaceError", "手机端人脸分析组件不可用。");
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                Bitmap bitmap = null;
                try {
                    byte[] bytes = Base64.decode(encodedJpeg, Base64.DEFAULT);
                    bitmap = BitmapFactory.decodeByteArray(bytes, 0, bytes.length);
                    if (bitmap == null) {
                        send(requestId, "OnNativeFaceError", "相机图像无法解析。");
                        return;
                    }

                    final Bitmap source = bitmap;
                    InputImage image = InputImage.fromBitmap(source, 0);
                    faceDetector.process(image)
                        .addOnSuccessListener(new OnSuccessListener<List<Face>>() {
                            @Override
                            public void onSuccess(List<Face> faces) {
                                try {
                                    if (faces == null || faces.size() == 0) {
                                        send(requestId, "OnNativeFaceError", "未检测到人脸，请正对前置摄像头。");
                                    } else if (faces.size() > 1) {
                                        send(requestId, "OnNativeFaceError", "检测到多张脸，请保持画面中只有一人。");
                                    } else {
                                        send(requestId, "OnNativeFaceResult", buildResult(faces.get(0)).toString());
                                    }
                                } catch (Exception ex) {
                                    send(requestId, "OnNativeFaceError", "手机端人脸分析失败：" + safeMessage(ex));
                                } finally {
                                    source.recycle();
                                }
                            }
                        })
                        .addOnFailureListener(new OnFailureListener() {
                            @Override
                            public void onFailure(Exception ex) {
                                source.recycle();
                                send(requestId, "OnNativeFaceError", "手机端人脸分析失败：" + safeMessage(ex));
                            }
                        });
                } catch (Exception ex) {
                    if (bitmap != null) {
                        bitmap.recycle();
                    }
                    send(requestId, "OnNativeFaceError", "手机端人脸分析启动失败：" + safeMessage(ex));
                }
            }
        });
    }

    public void shutdown() {
        if (faceDetector != null) {
            faceDetector.close();
            faceDetector = null;
        }
    }

    private JSONObject buildResult(Face face) throws Exception {
        float smile = face.getSmilingProbability();
        boolean hasSmileScore = smile >= 0.0f && smile <= 1.0f;
        float joyScore = hasSmileScore ? clamp(0.25f + 0.75f * smile) : 0.0f;
        boolean isJoy = hasSmileScore && smile >= 0.60f;
        float neutralScore = isJoy ? clamp(1.0f - joyScore * 0.45f) : (hasSmileScore ? clamp(0.60f + (1.0f - smile) * 0.25f) : 0.65f);

        JSONArray scores = new JSONArray();
        scores.put(score("joy", joyScore));
        scores.put(score("neutral", neutralScore));

        JSONObject result = new JSONObject();
        result.put("label", isJoy ? "joy" : "neutral");
        result.put("score", isJoy ? joyScore : neutralScore);
        result.put("source", "face");
        result.put("capturedAtUnixMs", System.currentTimeMillis());
        result.put("scores", scores);
        return result;
    }

    private JSONObject score(String label, float value) throws Exception {
        JSONObject score = new JSONObject();
        score.put("label", label);
        score.put("score", clamp(value));
        return score;
    }

    private float clamp(float value) {
        return Math.max(0.0f, Math.min(1.0f, value));
    }

    private String safeMessage(Exception ex) {
        return ex == null || ex.getMessage() == null ? "未知错误" : ex.getMessage();
    }

    private void send(String callbackId, String method, String message) {
        UnityPlayer.UnitySendMessage(unityObjectName, method, (callbackId == null ? "" : callbackId) + "\n" + (message == null ? "" : message));
    }
}
