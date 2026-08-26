package com.srtp.avatar;

import android.app.Activity;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Rect;
import android.util.Base64;

import ai.onnxruntime.OnnxTensor;
import ai.onnxruntime.OrtEnvironment;
import ai.onnxruntime.OrtSession;

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

import java.nio.FloatBuffer;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;

/**
 * Detects and crops one face with bundled ML Kit, then classifies its apparent
 * expression with the embedded HSEmotion ONNX model. Frames never leave the
 * process and are recycled immediately after inference.
 */
public final class NativeEmotionBridge {
    private static final int MODEL_INPUT_SIZE = 224;
    private static final String[] PROJECT_LABELS = {
        "joy", "angry", "sorrow", "surprised", "neutral"
    };
    // HSEmotion output order: anger, contempt, disgust, fear, happiness,
    // neutral, sadness, surprise. Unsupported classes are intentionally
    // excluded instead of being accumulated into neutral, which previously
    // gave neutral an artificial four-class advantage.
    private static final int[] PROJECT_MODEL_INDICES = {4, 0, 6, 7, 5};
    private static final float[] IMAGE_MEAN = {0.485f, 0.456f, 0.406f};
    private static final float[] IMAGE_STD = {0.229f, 0.224f, 0.225f};

    private final String unityObjectName;
    private final ExecutorService inferenceExecutor = Executors.newSingleThreadExecutor();
    private FaceDetector faceDetector;
    private volatile OrtSession emotionSession;
    private volatile String modelInputName;
    private volatile String modelError;
    private volatile boolean modelLoading = true;
    private volatile boolean shuttingDown;

    public NativeEmotionBridge(Activity activity, String unityObjectName, final byte[] modelBytes) {
        this.unityObjectName = unityObjectName;
        FaceDetectorOptions options = new FaceDetectorOptions.Builder()
            .setPerformanceMode(FaceDetectorOptions.PERFORMANCE_MODE_ACCURATE)
            .setMinFaceSize(0.10f)
            .build();
        faceDetector = FaceDetection.getClient(options);
        inferenceExecutor.execute(new Runnable() {
            @Override
            public void run() {
                initializeModel(modelBytes);
            }
        });
    }

    public void analyzeFace(final String requestId, final String encodedJpeg) {
        if (shuttingDown || faceDetector == null) {
            send(requestId, "OnNativeFaceError", "手机端人脸分析组件不可用。");
            return;
        }
        if (modelLoading) {
            send(requestId, "OnNativeFaceError", "手机端表情模型正在初始化，请稍候。");
            return;
        }
        if (emotionSession == null) {
            send(requestId, "OnNativeFaceError", "手机端表情模型不可用：" + safeText(modelError));
            return;
        }

        Bitmap bitmap = null;
        try {
            byte[] bytes = Base64.decode(encodedJpeg, Base64.DEFAULT);
            bitmap = BitmapFactory.decodeByteArray(bytes, 0, bytes.length);
            if (bitmap == null) {
                send(requestId, "OnNativeFaceError", "相机图像无法解析。");
                return;
            }

            final Bitmap source = bitmap;
            faceDetector.process(InputImage.fromBitmap(source, 0))
                .addOnSuccessListener(new OnSuccessListener<List<Face>>() {
                    @Override
                    public void onSuccess(List<Face> faces) {
                        if (faces == null || faces.size() == 0) {
                            source.recycle();
                            send(requestId, "OnNativeFaceError", "未检测到人脸，请正对前置摄像头。");
                        } else if (faces.size() > 1) {
                            source.recycle();
                            send(requestId, "OnNativeFaceError", "检测到多张脸，请保持画面中只有一人。");
                        } else {
                            classifyAsync(requestId, source, faces.get(0));
                        }
                    }
                })
                .addOnFailureListener(new OnFailureListener() {
                    @Override
                    public void onFailure(Exception ex) {
                        source.recycle();
                        send(requestId, "OnNativeFaceError", "手机端人脸检测失败：" + safeMessage(ex));
                    }
                });
        } catch (Exception ex) {
            if (bitmap != null) {
                bitmap.recycle();
            }
            send(requestId, "OnNativeFaceError", "手机端人脸分析启动失败：" + safeMessage(ex));
        }
    }

    public void shutdown() {
        shuttingDown = true;
        if (faceDetector != null) {
            faceDetector.close();
            faceDetector = null;
        }
        try {
            inferenceExecutor.execute(new Runnable() {
                @Override
                public void run() {
                    closeModel();
                }
            });
        } catch (RejectedExecutionException ignored) {
            closeModel();
        }
        inferenceExecutor.shutdown();
    }

    private void initializeModel(byte[] modelBytes) {
        try {
            if (modelBytes == null || modelBytes.length == 0) {
                throw new IllegalArgumentException("模型资源为空");
            }
            OrtEnvironment environment = OrtEnvironment.getEnvironment();
            try (OrtSession.SessionOptions options = new OrtSession.SessionOptions()) {
                options.setOptimizationLevel(OrtSession.SessionOptions.OptLevel.ALL_OPT);
                emotionSession = environment.createSession(modelBytes, options);
                modelInputName = emotionSession.getInputNames().iterator().next();
            }
        } catch (Throwable ex) {
            modelError = safeMessage(ex);
            closeModel();
        } finally {
            modelLoading = false;
        }
    }

    private void classifyAsync(final String requestId, final Bitmap source, final Face face) {
        try {
            inferenceExecutor.execute(new Runnable() {
                @Override
                public void run() {
                    try {
                        if (shuttingDown || emotionSession == null) {
                            send(requestId, "OnNativeFaceError", "手机端表情模型不可用。");
                            return;
                        }
                        send(requestId, "OnNativeFaceResult", buildResult(source, face).toString());
                    } catch (Throwable ex) {
                        send(requestId, "OnNativeFaceError", "手机端表情推理失败：" + safeMessage(ex));
                    } finally {
                        source.recycle();
                    }
                }
            });
        } catch (RejectedExecutionException ex) {
            source.recycle();
            send(requestId, "OnNativeFaceError", "手机端表情推理已停止。");
        }
    }

    private JSONObject buildResult(Bitmap source, Face face) throws Exception {
        float[] logits = runModel(cropAndPrepare(source, face.getBoundingBox()));
        float[] projected = projectSupportedEmotions(logits);

        int bestIndex = 0;
        for (int index = 1; index < projected.length; index++) {
            if (projected[index] > projected[bestIndex]) {
                bestIndex = index;
            }
        }

        JSONArray scores = new JSONArray();
        for (int index = 0; index < PROJECT_LABELS.length; index++) {
            scores.put(score(PROJECT_LABELS[index], projected[index]));
        }

        JSONObject result = new JSONObject();
        result.put("label", PROJECT_LABELS[bestIndex]);
        result.put("score", clamp(projected[bestIndex]));
        result.put("source", "face");
        result.put("capturedAtUnixMs", System.currentTimeMillis());
        result.put("scores", scores);
        return result;
    }

    private float[] projectSupportedEmotions(float[] modelLogits) {
        if (modelLogits == null || modelLogits.length != 8) {
            throw new IllegalArgumentException("模型类别数量不正确");
        }

        float[] supportedLogits = new float[PROJECT_MODEL_INDICES.length];
        for (int index = 0; index < PROJECT_MODEL_INDICES.length; index++) {
            supportedLogits[index] = modelLogits[PROJECT_MODEL_INDICES[index]];
        }
        return softmax(supportedLogits);
    }

    private float[] cropAndPrepare(Bitmap source, Rect detectedBounds) {
        int padX = Math.round(detectedBounds.width() * 0.18f);
        int padTop = Math.round(detectedBounds.height() * 0.24f);
        int padBottom = Math.round(detectedBounds.height() * 0.18f);
        int left = Math.max(0, detectedBounds.left - padX);
        int top = Math.max(0, detectedBounds.top - padTop);
        int right = Math.min(source.getWidth(), detectedBounds.right + padX);
        int bottom = Math.min(source.getHeight(), detectedBounds.bottom + padBottom);
        if (right <= left || bottom <= top) {
            throw new IllegalArgumentException("人脸区域无效");
        }

        Bitmap crop = null;
        Bitmap scaled = null;
        try {
            crop = Bitmap.createBitmap(source, left, top, right - left, bottom - top);
            scaled = Bitmap.createScaledBitmap(crop, MODEL_INPUT_SIZE, MODEL_INPUT_SIZE, true);
            int[] pixels = new int[MODEL_INPUT_SIZE * MODEL_INPUT_SIZE];
            scaled.getPixels(pixels, 0, MODEL_INPUT_SIZE, 0, 0, MODEL_INPUT_SIZE, MODEL_INPUT_SIZE);

            int planeSize = MODEL_INPUT_SIZE * MODEL_INPUT_SIZE;
            float[] input = new float[planeSize * 3];
            for (int index = 0; index < pixels.length; index++) {
                int color = pixels[index];
                input[index] = (((color >> 16) & 0xff) / 255.0f - IMAGE_MEAN[0]) / IMAGE_STD[0];
                input[planeSize + index] = (((color >> 8) & 0xff) / 255.0f - IMAGE_MEAN[1]) / IMAGE_STD[1];
                input[planeSize * 2 + index] = ((color & 0xff) / 255.0f - IMAGE_MEAN[2]) / IMAGE_STD[2];
            }
            return input;
        } finally {
            if (scaled != null && scaled != crop) {
                scaled.recycle();
            }
            if (crop != null) {
                crop.recycle();
            }
        }
    }

    private float[] runModel(float[] input) throws Exception {
        OrtSession session = emotionSession;
        if (session == null || modelInputName == null) {
            throw new IllegalStateException("模型尚未初始化");
        }
        long[] shape = {1, 3, MODEL_INPUT_SIZE, MODEL_INPUT_SIZE};
        try (OnnxTensor tensor = OnnxTensor.createTensor(
                OrtEnvironment.getEnvironment(), FloatBuffer.wrap(input), shape);
             OrtSession.Result output = session.run(Collections.singletonMap(modelInputName, tensor))) {
            Object value = output.get(0).getValue();
            if (value instanceof float[][]) {
                float[][] rows = (float[][]) value;
                if (rows.length == 1 && rows[0].length == 8) {
                    return rows[0];
                }
            }
            throw new IllegalStateException("模型输出格式不正确");
        }
    }

    private float[] softmax(float[] values) {
        float maximum = values[0];
        for (int index = 1; index < values.length; index++) {
            maximum = Math.max(maximum, values[index]);
        }
        float total = 0.0f;
        float[] result = new float[values.length];
        for (int index = 0; index < values.length; index++) {
            result[index] = (float) Math.exp(values[index] - maximum);
            total += result[index];
        }
        for (int index = 0; index < result.length; index++) {
            result[index] /= total;
        }
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

    private void closeModel() {
        OrtSession session = emotionSession;
        emotionSession = null;
        modelInputName = null;
        if (session != null) {
            try {
                session.close();
            } catch (Exception ignored) {
            }
        }
    }

    private String safeText(String value) {
        return value == null || value.trim().isEmpty() ? "未知错误" : value;
    }

    private String safeMessage(Throwable ex) {
        return ex == null || ex.getMessage() == null ? "未知错误" : ex.getMessage();
    }

    private void send(String callbackId, String method, String message) {
        UnityPlayer.UnitySendMessage(
            unityObjectName,
            method,
            (callbackId == null ? "" : callbackId) + "\n" + (message == null ? "" : message));
    }
}
