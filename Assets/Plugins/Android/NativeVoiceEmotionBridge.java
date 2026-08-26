package com.srtp.avatar;

import android.app.Activity;
import android.util.Base64;
import android.util.Log;
import ai.onnxruntime.OnnxTensor;
import ai.onnxruntime.OrtEnvironment;
import ai.onnxruntime.OrtSession;
import com.unity3d.player.UnityPlayer;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.nio.FloatBuffer;
import java.util.Collections;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;

/** Runs the bundled INT8 speech-emotion model entirely on the Android device. */
public final class NativeVoiceEmotionBridge {
    private static final String TAG = "NativeVoiceEmotionBridge";
    private static final String MODEL_ASSET = "speech_emotion_wav2vec2_int8.onnx";
    private static final int SAMPLE_RATE = 16000;
    private static final int MIN_SAMPLES = SAMPLE_RATE;
    private static final float MIN_RMS = 0.008f;
    private static final String[] LABELS = {"joy", "angry", "sorrow", "surprised", "neutral"};
    private static final int[] PROJECTED_INDICES = {4, 0, 6, 7};

    private final String unityObjectName;
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private volatile OrtSession session;
    private volatile String inputName;
    private volatile String modelError;
    private volatile boolean loading = true;
    private volatile boolean stopped;

    public NativeVoiceEmotionBridge(final Activity activity, String unityObjectName) {
        this.unityObjectName = unityObjectName;
        executor.execute(new Runnable() {
            @Override public void run() { initialize(activity); }
        });
    }

    public void analyzeVoice(final String requestId, final String encodedWav) {
        if (stopped) {
            send(requestId, "OnNativeVoiceError", "手机端语音情绪分析已停止。");
            return;
        }
        if (loading) {
            send(requestId, "OnNativeVoiceError", "手机端语音情绪模型正在初始化，请稍候。");
            return;
        }
        if (session == null) {
            send(requestId, "OnNativeVoiceError", "手机端语音情绪模型不可用：" + safeText(modelError));
            return;
        }
        try {
            final float[] samples = decodeMonoPcm16Wav(Base64.decode(encodedWav, Base64.DEFAULT));
            if (samples.length < MIN_SAMPLES) {
                send(requestId, "OnNativeVoiceError", "语音片段过短，请连续说一句完整的话。");
                return;
            }
            if (rootMeanSquare(samples) < MIN_RMS) {
                send(requestId, "OnNativeVoiceError", "未检测到清晰语音，请靠近麦克风说话。");
                return;
            }
            executor.execute(new Runnable() {
                @Override public void run() {
                    try {
                        if (stopped || session == null || inputName == null) {
                            send(requestId, "OnNativeVoiceError", "手机端语音情绪模型不可用。");
                            return;
                        }
                        send(requestId, "OnNativeVoiceResult", buildResult(samples).toString());
                    } catch (Throwable ex) {
                        send(requestId, "OnNativeVoiceError", "手机端语音情绪推理失败：" + safeMessage(ex));
                    }
                }
            });
        } catch (RejectedExecutionException ex) {
            send(requestId, "OnNativeVoiceError", "手机端语音情绪推理已停止。");
        } catch (Throwable ex) {
            send(requestId, "OnNativeVoiceError", "语音数据无法解析：" + safeMessage(ex));
        }
    }

    public void shutdown() {
        stopped = true;
        try {
            executor.execute(new Runnable() {
                @Override public void run() { closeSession(); }
            });
        } catch (RejectedExecutionException ignored) {
            closeSession();
        }
        executor.shutdown();
    }

    private void initialize(Activity activity) {
        try {
            byte[] modelBytes = readAsset(activity, MODEL_ASSET);
            OrtEnvironment environment = OrtEnvironment.getEnvironment();
            try (OrtSession.SessionOptions options = new OrtSession.SessionOptions()) {
                options.setOptimizationLevel(OrtSession.SessionOptions.OptLevel.ALL_OPT);
                session = environment.createSession(modelBytes, options);
                inputName = session.getInputNames().iterator().next();
            }
            Log.i(TAG, "Voice emotion model ready");
        } catch (Throwable ex) {
            modelError = safeMessage(ex);
            closeSession();
            Log.e(TAG, "Voice emotion model unavailable", ex);
        } finally {
            loading = false;
        }
    }

    private JSONObject buildResult(float[] samples) throws Exception {
        float[] logits = runModel(samples);
        if (logits == null || logits.length != 8) throw new IllegalStateException("模型类别数量不正确");
        float[] projected = new float[LABELS.length];
        for (int index = 0; index < PROJECTED_INDICES.length; index++) projected[index] = logits[PROJECTED_INDICES[index]];
        // Calm and neutral are alternatives, so max avoids a double-logit bias.
        projected[4] = Math.max(logits[1], logits[5]);
        float[] scores = softmax(projected);
        int best = 0;
        for (int index = 1; index < scores.length; index++) if (scores[index] > scores[best]) best = index;
        JSONArray allScores = new JSONArray();
        for (int index = 0; index < LABELS.length; index++) {
            JSONObject item = new JSONObject();
            item.put("label", LABELS[index]);
            item.put("score", clamp(scores[index]));
            allScores.put(item);
        }
        JSONObject result = new JSONObject();
        result.put("label", LABELS[best]);
        result.put("score", clamp(scores[best]));
        result.put("source", "voice");
        result.put("capturedAtUnixMs", System.currentTimeMillis());
        result.put("scores", allScores);
        return result;
    }

    private float[] runModel(float[] input) throws Exception {
        OrtSession activeSession = session;
        try (OnnxTensor tensor = OnnxTensor.createTensor(OrtEnvironment.getEnvironment(), FloatBuffer.wrap(input), new long[] {1, input.length});
             OrtSession.Result output = activeSession.run(Collections.singletonMap(inputName, tensor))) {
            Object value = output.get(0).getValue();
            if (value instanceof float[][]) {
                float[][] rows = (float[][]) value;
                if (rows.length == 1) return rows[0];
            }
            throw new IllegalStateException("模型输出格式不正确");
        }
    }

    private float[] decodeMonoPcm16Wav(byte[] wav) {
        if (wav == null || wav.length < 44 || !hasAscii(wav, 0, "RIFF") || !hasAscii(wav, 8, "WAVE")) throw new IllegalArgumentException("需要 16 kHz 单声道 PCM WAV");
        int cursor = 12, audioFormat = 0, channels = 0, sampleRate = 0, bitsPerSample = 0;
        int dataOffset = -1, dataLength = 0;
        while (cursor + 8 <= wav.length) {
            int length = littleEndianInt(wav, cursor + 4);
            int content = cursor + 8;
            if (length < 0 || content + length > wav.length) throw new IllegalArgumentException("WAV 数据不完整");
            if (hasAscii(wav, cursor, "fmt ") && length >= 16) {
                audioFormat = littleEndianShort(wav, content);
                channels = littleEndianShort(wav, content + 2);
                sampleRate = littleEndianInt(wav, content + 4);
                bitsPerSample = littleEndianShort(wav, content + 14);
            } else if (hasAscii(wav, cursor, "data")) {
                dataOffset = content; dataLength = length; break;
            }
            cursor = content + length + (length & 1);
        }
        if (audioFormat != 1 || channels != 1 || sampleRate != SAMPLE_RATE || bitsPerSample != 16 || dataOffset < 0 || dataLength < 2) throw new IllegalArgumentException("仅支持 16 kHz 单声道 PCM16 WAV");
        float[] samples = new float[dataLength / 2];
        for (int index = 0; index < samples.length; index++) samples[index] = littleEndianSignedShort(wav, dataOffset + index * 2) / 32768.0f;
        return samples;
    }

    private byte[] readAsset(Activity activity, String name) throws Exception {
        try (InputStream input = activity.getAssets().open(name); ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[64 * 1024]; int count;
            while ((count = input.read(buffer)) != -1) output.write(buffer, 0, count);
            return output.toByteArray();
        }
    }

    private float rootMeanSquare(float[] samples) {
        double total = 0.0; for (float sample : samples) total += sample * sample;
        return samples.length == 0 ? 0.0f : (float) Math.sqrt(total / samples.length);
    }
    private boolean hasAscii(byte[] bytes, int offset, String value) {
        if (offset < 0 || offset + value.length() > bytes.length) return false;
        for (int index = 0; index < value.length(); index++) if ((byte) value.charAt(index) != bytes[offset + index]) return false;
        return true;
    }
    private int littleEndianShort(byte[] bytes, int offset) { return (bytes[offset] & 0xff) | ((bytes[offset + 1] & 0xff) << 8); }
    private short littleEndianSignedShort(byte[] bytes, int offset) { return (short) littleEndianShort(bytes, offset); }
    private int littleEndianInt(byte[] bytes, int offset) {
        return (bytes[offset] & 0xff) | ((bytes[offset + 1] & 0xff) << 8) | ((bytes[offset + 2] & 0xff) << 16) | ((bytes[offset + 3] & 0xff) << 24);
    }
    private float[] softmax(float[] values) {
        float maximum = values[0]; for (int index = 1; index < values.length; index++) maximum = Math.max(maximum, values[index]);
        float total = 0.0f; float[] result = new float[values.length];
        for (int index = 0; index < values.length; index++) { result[index] = (float) Math.exp(values[index] - maximum); total += result[index]; }
        for (int index = 0; index < result.length; index++) result[index] /= total;
        return result;
    }
    private float clamp(float value) { return Math.max(0.0f, Math.min(1.0f, value)); }
    private void closeSession() {
        OrtSession activeSession = session; session = null; inputName = null;
        if (activeSession != null) try { activeSession.close(); } catch (Exception ignored) { }
    }
    private String safeText(String value) { return value == null || value.trim().isEmpty() ? "未知错误" : value; }
    private String safeMessage(Throwable ex) { return ex == null || ex.getMessage() == null ? "未知错误" : ex.getMessage(); }
    private void send(String requestId, String method, String message) {
        UnityPlayer.UnitySendMessage(unityObjectName, method, (requestId == null ? "" : requestId) + "\n" + (message == null ? "" : message));
    }
}
