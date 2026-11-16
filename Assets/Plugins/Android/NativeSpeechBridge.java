package com.srtp.avatar;

import android.app.Activity;
import android.content.Intent;
import android.os.Build;
import android.os.Bundle;
import android.os.SystemClock;
import android.media.AudioFormat;
import android.media.AudioRecord;
import android.media.MediaRecorder;
import android.util.Base64;
import android.speech.RecognitionListener;
import android.speech.RecognizerIntent;
import android.speech.SpeechRecognizer;
import android.speech.tts.TextToSpeech;
import android.speech.tts.UtteranceProgressListener;

import com.unity3d.player.UnityPlayer;

import org.json.JSONException;
import org.json.JSONObject;
import org.json.JSONArray;
import org.vosk.Model;
import org.vosk.Recognizer;
import org.vosk.android.StorageService;

import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.Locale;
import java.util.concurrent.ConcurrentHashMap;

public final class NativeSpeechBridge {
    private final Activity activity;
    private final String unityObjectName;
    private SpeechRecognizer speechRecognizer;
    private TextToSpeech textToSpeech;
    private boolean ttsReady;
    private volatile String activeUtteranceId;
    private final ConcurrentHashMap<String, PcmEnvelope> ttsEnvelopes = new ConcurrentHashMap<>();
    private boolean usingOnDeviceRecognizer;
    private String activeSystemSessionId;

    private static final String OFFLINE_MODEL_ASSET = "vosk-model-small-cn-0.22";
    private static final String OFFLINE_MODEL_STORAGE = "srtp-vosk-model";
    private static final float OFFLINE_SAMPLE_RATE = 16000.0f;
    private static final int OFFLINE_LISTEN_TIMEOUT_MILLIS = 15000;
    private static final int OFFLINE_MAX_CAPTURE_BYTES = (int) OFFLINE_SAMPLE_RATE * 2 * 16;
    private static final int OFFLINE_MAX_EMOTION_MILLIS = 8000;
    private static final int VOICE_ACTIVITY_SAMPLE_THRESHOLD = 350;

    private Model offlineModel;
    private Recognizer offlineRecognizer;
    private AudioRecord offlineAudioRecord;
    private Thread offlineAudioThread;
    private ByteArrayOutputStream offlinePcm;
    private volatile boolean offlineListening;
    private String offlineSessionId;
    private boolean offlineModelLoading;
    private String offlineInitError = "";

    public NativeSpeechBridge(Activity activity, String unityObjectName) {
        this.activity = activity;
        this.unityObjectName = unityObjectName;
        initializeTextToSpeech();
        initializeOfflineModel();
    }

    public boolean isSpeechRecognitionAvailable() {
        return activity != null && (isSystemRecognitionAvailable() || offlineModel != null || offlineModelLoading);
    }

    public boolean isTextToSpeechAvailable() {
        return activity != null && textToSpeech != null && ttsReady;
    }

    public void startRecognition(final String languageTag, final String sessionId, final boolean preferOnDevice) {
        if (activity == null) {
            send(sessionId, "OnAndroidSpeechError", "Android Activity 不可用。");
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    // Prefer the bundled Vosk model whenever it is ready. This avoids relying on
                    // device-specific speech services that may report ready but drop the first utterance.
                    if (preferOnDevice && offlineModel != null && startOfflineRecognition(sessionId)) {
                        return;
                    }
                    if (preferOnDevice && offlineModelLoading) {
                        send(sessionId, "OnAndroidSpeechError", "离线语音模型正在准备，请稍后重试。");
                        return;
                    }
                    if (!isSystemRecognitionAvailable()) {
                        releaseSystemSpeechRecognizer();
                        if (startOfflineRecognition(sessionId)) {
                            return;
                        }
                        send(sessionId, "OnAndroidSpeechError", offlineUnavailableMessage());
                        return;
                    }
                    stopOfflineRecognition();
                    releaseSystemSpeechRecognizer();
                    speechRecognizer = createSpeechRecognizer(preferOnDevice);
                    activeSystemSessionId = sessionId;
                    speechRecognizer.setRecognitionListener(new UnityRecognitionListener(sessionId));

                    Intent intent = new Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH);
                    intent.putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM);
                    intent.putExtra(RecognizerIntent.EXTRA_LANGUAGE, normalizeLanguage(languageTag));
                    intent.putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, true);
                    intent.putExtra(RecognizerIntent.EXTRA_PREFER_OFFLINE, usingOnDeviceRecognizer);
                    intent.putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 3);
                    // These are advisory values, but on supported recognizers they make a
                    // spoken turn finish promptly without requiring the user to tap again.
                    intent.putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_MINIMUM_LENGTH_MILLIS, 500);
                    intent.putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_POSSIBLY_COMPLETE_SILENCE_LENGTH_MILLIS, 650);
                    intent.putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS, 850);
                    speechRecognizer.startListening(intent);
                } catch (Exception ex) {
                    if (sessionId.equals(activeSystemSessionId)) {
                        releaseSystemSpeechRecognizer();
                    }
                    send(sessionId, "OnAndroidSpeechError", ex.getMessage());
                }
            }
        });
    }

    public void cancelRecognition() {
        if (activity == null) {
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                stopOfflineRecognition();
                releaseSystemSpeechRecognizer();
            }
        });
    }

    public void speak(final String text, final String languageTag, final String utteranceId) {
        if (text == null || text.trim().length() == 0) {
            send(utteranceId, "OnAndroidTtsError", "没有可播报的回复内容。");
            return;
        }
        if (activity == null) {
            send(utteranceId, "OnAndroidTtsError", "Android Activity 不可用。");
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    if (textToSpeech == null) {
                        initializeTextToSpeech();
                    }

                    if (!ttsReady) {
                        send(utteranceId, "OnAndroidTtsError", "系统 TTS 还没有初始化完成，请稍后再试。");
                        return;
                    }

                    Locale locale = localeFromTag(languageTag);
                    textToSpeech.setLanguage(locale);
                    Bundle params = new Bundle();
                    params.putString(TextToSpeech.Engine.KEY_PARAM_UTTERANCE_ID, utteranceId);
                    activeUtteranceId = utteranceId;
                    ttsEnvelopes.clear();
                    int result = textToSpeech.speak(text, TextToSpeech.QUEUE_FLUSH, params, utteranceId);
                    if (result == TextToSpeech.ERROR) {
                        send(utteranceId, "OnAndroidTtsError", "系统 TTS 播报启动失败。");
                    }
                } catch (Exception ex) {
                    send(utteranceId, "OnAndroidTtsError", ex.getMessage());
                }
            }
        });
    }

    public void stopSpeaking() {
        activeUtteranceId = null;
        ttsEnvelopes.clear();
        if (activity == null) {
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (textToSpeech != null) {
                    textToSpeech.stop();
                }
            }
        });
    }

    public void shutdown() {
        if (activity == null) {
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                stopOfflineRecognition();
                releaseSystemSpeechRecognizer();
                if (offlineModel != null) {
                    offlineModel.close();
                    offlineModel = null;
                }

                if (textToSpeech != null) {
                    textToSpeech.stop();
                    textToSpeech.shutdown();
                    textToSpeech = null;
                    ttsReady = false;
                }
            }
        });
    }

    private void initializeTextToSpeech() {
        if (activity == null || textToSpeech != null) {
            return;
        }

        textToSpeech = new TextToSpeech(activity.getApplicationContext(), new TextToSpeech.OnInitListener() {
            @Override
            public void onInit(int status) {
                ttsReady = status == TextToSpeech.SUCCESS;
                if (ttsReady) {
                    textToSpeech.setLanguage(Locale.CHINA);
                    textToSpeech.setOnUtteranceProgressListener(new UtteranceProgressListener() {
                        @Override
                        public void onBeginSynthesis(String utteranceId, int sampleRate, int audioFormat, int channels) {
                            if (utteranceId.equals(activeUtteranceId))
                                ttsEnvelopes.put(utteranceId, new PcmEnvelope(sampleRate, audioFormat, channels));
                        }

                        @Override
                        public void onAudioAvailable(String utteranceId, byte[] audio) {
                            PcmEnvelope envelope = ttsEnvelopes.get(utteranceId);
                            if (envelope != null) envelope.append(utteranceId, audio);
                        }

                        @Override
                        public void onRangeStart(String utteranceId, int start, int end, int frame) {
                            PcmEnvelope envelope = ttsEnvelopes.get(utteranceId);
                            if (envelope != null && envelope.sampleRate > 0)
                                send(utteranceId, "OnAndroidTtsPlaybackPosition", Float.toString(frame / (float) envelope.sampleRate));
                        }

                        @Override
                        public void onStart(String utteranceId) {
                            send(utteranceId, "OnAndroidTtsStarted", Long.toString(SystemClock.elapsedRealtime()));
                        }

                        @Override
                        public void onDone(String utteranceId) {
                            ttsEnvelopes.remove(utteranceId);
                            send(utteranceId, "OnAndroidTtsDone", "");
                        }

                        @Override
                        public void onError(String utteranceId) {
                            ttsEnvelopes.remove(utteranceId);
                            send(utteranceId, "OnAndroidTtsError", "系统 TTS 播报失败。");
                        }
                    });
                }
            }
        });
    }

    // Compact 20 ms RMS bins; no PCM or recognized text is sent back for mouth animation.
    private final class PcmEnvelope {
        final int sampleRate;
        final int format;
        final int bytesPerSample;
        final int samplesPerBin;
        final float stepSeconds;
        int sampleBits;
        int sampleByteCount;
        int sampleCount;
        int binIndex;
        double energy;

        PcmEnvelope(int sampleRate, int format, int channels) {
            this.sampleRate = sampleRate;
            this.format = format;
            bytesPerSample = format == AudioFormat.ENCODING_PCM_8BIT ? 1
                : format == AudioFormat.ENCODING_PCM_16BIT ? 2
                : format == AudioFormat.ENCODING_PCM_FLOAT ? 4 : 0;
            int frames = Math.max(1, sampleRate / 50);
            samplesPerBin = frames * Math.max(1, channels);
            stepSeconds = sampleRate > 0 ? frames / (float) sampleRate : 0.02f;
        }

        synchronized void append(String utteranceId, byte[] audio) {
            if (bytesPerSample == 0 || sampleRate <= 0 || binIndex >= 6000) return;
            try {
                JSONArray levels = new JSONArray();
                int firstBin = binIndex;
                for (byte value : audio) {
                    sampleBits |= (value & 0xff) << (sampleByteCount * 8);
                    if (++sampleByteCount != bytesPerSample) continue;
                    float sample = format == AudioFormat.ENCODING_PCM_8BIT ? (sampleBits - 128) / 128f
                        : format == AudioFormat.ENCODING_PCM_16BIT ? (short) sampleBits / 32768f
                        : Float.intBitsToFloat(sampleBits);
                    sampleBits = 0;
                    sampleByteCount = 0;
                    if (Float.isNaN(sample) || Float.isInfinite(sample)) sample = 0f;
                    energy += sample * sample;
                    if (++sampleCount >= samplesPerBin) {
                        float level = (float) Math.max(0, Math.min(1, (Math.sqrt(energy / sampleCount) - 0.008) * 14));
                        levels.put((double) level);
                        energy = 0;
                        sampleCount = 0;
                        if (++binIndex >= 6000) break;
                    }
                }
                if (levels.length() == 0) return;
                JSONObject result = new JSONObject();
                result.put("startIndex", firstBin);
                result.put("stepSeconds", stepSeconds);
                result.put("levels", levels);
                send(utteranceId, "OnAndroidTtsEnvelope", result.toString());
            } catch (JSONException ex) {
                android.util.Log.w("SRTP", "Could not encode TTS mouth envelope", ex);
            }
        }
    }

    private void initializeOfflineModel() {
        if (activity == null || offlineModel != null || offlineModelLoading) {
            return;
        }

        offlineModelLoading = true;
        offlineInitError = "";
        try {
            StorageService.unpack(
                activity.getApplicationContext(),
                OFFLINE_MODEL_ASSET,
                OFFLINE_MODEL_STORAGE,
                model -> {
                    offlineModel = model;
                    offlineModelLoading = false;
                    offlineInitError = "";
                },
                exception -> {
                    offlineModelLoading = false;
                    offlineInitError = "离线语音模型加载失败：" + safeExceptionMessage(exception);
                });
        } catch (Exception ex) {
            offlineModelLoading = false;
            offlineInitError = "离线语音模型加载失败：" + safeExceptionMessage(ex);
        }
    }

    private boolean isSystemRecognitionAvailable() {
        return activity != null && (SpeechRecognizer.isRecognitionAvailable(activity) || isOnDeviceRecognitionAvailable());
    }

    private boolean startOfflineRecognition(String sessionId) {
        if (offlineModel == null) {
            initializeOfflineModel();
            return false;
        }

        releaseSystemSpeechRecognizer();
        stopOfflineRecognition();
        try {
            offlineRecognizer = new Recognizer(offlineModel, OFFLINE_SAMPLE_RATE);
            // A short end-of-turn delay keeps automatic conversation responsive
            // while still allowing normal Chinese sentence-final pauses.
            offlineRecognizer.setEndpointerDelays(4.0f, 0.55f, 15.0f);
            offlineSessionId = sessionId;
            offlinePcm = new ByteArrayOutputStream();
            if (!startOfflineAudioLoop(sessionId)) {
                stopOfflineRecognition();
                return false;
            }
            send(sessionId, "OnAndroidSpeechReady", "");
            return true;
        } catch (Exception ex) {
            offlineInitError = "离线语音识别启动失败：" + safeExceptionMessage(ex);
            stopOfflineRecognition();
            return false;
        }
    }

    private boolean startOfflineAudioLoop(final String sessionId) {
        int minBuffer = AudioRecord.getMinBufferSize(
            (int) OFFLINE_SAMPLE_RATE,
            AudioFormat.CHANNEL_IN_MONO,
            AudioFormat.ENCODING_PCM_16BIT);
        if (minBuffer <= 0) {
            offlineInitError = "手机麦克风录音缓冲区不可用。";
            return false;
        }

        int bufferSize = Math.max(minBuffer * 2, 4096);
        offlineAudioRecord = new AudioRecord(
            MediaRecorder.AudioSource.VOICE_RECOGNITION,
            (int) OFFLINE_SAMPLE_RATE,
            AudioFormat.CHANNEL_IN_MONO,
            AudioFormat.ENCODING_PCM_16BIT,
            bufferSize);
        if (offlineAudioRecord.getState() != AudioRecord.STATE_INITIALIZED) {
            offlineInitError = "手机麦克风录音初始化失败。";
            return false;
        }

        offlineListening = true;
        offlineAudioRecord.startRecording();
        offlineAudioThread = new Thread(new Runnable() {
            @Override public void run() {
                readOfflineAudio(sessionId);
            }
        }, "SrtpVoskAudio");
        offlineAudioThread.start();
        return true;
    }

    private void readOfflineAudio(String sessionId) {
        byte[] buffer = new byte[4096];
        long startedAt = SystemClock.elapsedRealtime();
        long lastReadAt = -1;
        try {
            while (offlineListening && sessionId.equals(offlineSessionId) && offlineAudioRecord != null) {
                int count = offlineAudioRecord.read(buffer, 0, buffer.length);
                if (count > 0) {
                    lastReadAt = SystemClock.elapsedRealtime();
                    appendOfflinePcm(buffer, count);
                    Recognizer recognizer = offlineRecognizer;
                    if (recognizer != null && recognizer.acceptWaveForm(buffer, count)) {
                        String text = extractVoskText(recognizer.getResult());
                        if (text.length() > 0) {
                            finishOfflineRecognition(sessionId, text, null, lastReadAt);
                            return;
                        }
                    }
                }

                if (SystemClock.elapsedRealtime() - startedAt >= OFFLINE_LISTEN_TIMEOUT_MILLIS) {
                    Recognizer recognizer = offlineRecognizer;
                    String finalText = recognizer == null ? "" : extractVoskText(recognizer.getFinalResult());
                    finishOfflineRecognition(sessionId, finalText, finalText.length() == 0 ? "没有听到说话。" : null, lastReadAt);
                    return;
                }
            }
        } catch (JSONException ex) {
            if (sessionId.equals(offlineSessionId)) {
                finishOfflineRecognition(sessionId, null, "离线语音模型返回格式无效：" + safeExceptionMessage(ex), lastReadAt);
            }
        } catch (Throwable ex) {
            if (sessionId.equals(offlineSessionId)) {
                finishOfflineRecognition(sessionId, null, "离线语音识别失败：" + safeThrowableMessage(ex), lastReadAt);
            }
        }
    }

    private void appendOfflinePcm(byte[] buffer, int count) {
        if (offlinePcm == null || count <= 0) {
            return;
        }

        int writable = Math.min(count, Math.max(0, OFFLINE_MAX_CAPTURE_BYTES - offlinePcm.size()));
        if (writable > 0) {
            offlinePcm.write(buffer, 0, writable);
        }
    }

    private void stopOfflineRecognition() {
        offlineSessionId = null;
        offlineListening = false;

        AudioRecord recorder = offlineAudioRecord;
        offlineAudioRecord = null;
        if (recorder != null) {
            try {
                recorder.stop();
            } catch (Exception ignored) {
            }
            try {
                recorder.release();
            } catch (Exception ignored) {
            }
        }

        Thread reader = offlineAudioThread;
        offlineAudioThread = null;
        if (reader != null && reader != Thread.currentThread()) {
            reader.interrupt();
        }

        if (offlineRecognizer != null) {
            offlineRecognizer.close();
            offlineRecognizer = null;
        }
        offlinePcm = null;
    }

    private void finishOfflineRecognition(String sessionId, String text, String error, long lastReadAt) {
        if (offlineSessionId == null || !offlineSessionId.equals(sessionId)) {
            return;
        }

        long recognizedAt = SystemClock.elapsedRealtime();
        long speechEndedAt = -1;
        byte[] speechWav = null;
        ByteArrayOutputStream recorded = offlinePcm;
        if (error == null && recorded != null) {
            byte[] pcm = recorded.toByteArray();
            int last = findActiveSample(pcm, pcm.length / 2 - 1, -1);
            if (last >= 0) {
                if (lastReadAt >= 0 && pcm.length < OFFLINE_MAX_CAPTURE_BYTES) {
                    // Anchor the last active sample to the most recent AudioRecord read.
                    int trailingSamples = pcm.length / 2 - last - 1;
                    speechEndedAt = lastReadAt - Math.round(trailingSamples * 1000.0 / OFFLINE_SAMPLE_RATE);
                }
                speechWav = createSpeechWav(pcm, findActiveSample(pcm, 0, 1), last);
            }
        }
        stopOfflineRecognition();
        if (error != null) {
            send(sessionId, "OnAndroidSpeechError", error);
        } else if (text == null || text.trim().length() == 0) {
            send(sessionId, "OnAndroidSpeechError", "没有识别到语音内容。");
        } else {
            if (speechWav != null && speechWav.length > 0) {
                send(sessionId, "OnAndroidSpeechAudio", Base64.encodeToString(speechWav, Base64.NO_WRAP));
            }
            sendSpeechResult(sessionId, "vosk", speechEndedAt, recognizedAt, text.trim());
        }
    }

    private byte[] createSpeechWav(byte[] pcm, int first, int last) {
        byte[] trimmed = trimPcmToSpeech(pcm, first, last);
        if (trimmed == null || trimmed.length < (int) OFFLINE_SAMPLE_RATE * 2) {
            return null;
        }

        ByteBuffer wav = ByteBuffer.allocate(44 + trimmed.length).order(ByteOrder.LITTLE_ENDIAN);
        wav.put(new byte[] {'R', 'I', 'F', 'F'});
        wav.putInt(36 + trimmed.length);
        wav.put(new byte[] {'W', 'A', 'V', 'E'});
        wav.put(new byte[] {'f', 'm', 't', ' '});
        wav.putInt(16);
        wav.putShort((short) 1);
        wav.putShort((short) 1);
        wav.putInt((int) OFFLINE_SAMPLE_RATE);
        wav.putInt((int) OFFLINE_SAMPLE_RATE * 2);
        wav.putShort((short) 2);
        wav.putShort((short) 16);
        wav.put(new byte[] {'d', 'a', 't', 'a'});
        wav.putInt(trimmed.length);
        wav.put(trimmed);
        return wav.array();
    }

    private static int findActiveSample(byte[] pcm, int start, int step) {
        int sampleCount = pcm.length / 2;
        for (int sample = start; sample >= 0 && sample < sampleCount; sample += step) {
            int byteIndex = sample * 2;
            short value = (short) ((pcm[byteIndex] & 0xff) | (pcm[byteIndex + 1] << 8));
            if (Math.abs((int) value) >= VOICE_ACTIVITY_SAMPLE_THRESHOLD) {
                return sample;
            }
        }
        return -1;
    }

    private byte[] trimPcmToSpeech(byte[] pcm, int first, int last) {
        int sampleCount = pcm.length / 2;
        int padding = (int) (OFFLINE_SAMPLE_RATE * 0.25f);
        int minimumSamples = (int) OFFLINE_SAMPLE_RATE;
        int maximumSamples = (int) OFFLINE_SAMPLE_RATE * OFFLINE_MAX_EMOTION_MILLIS / 1000;
        int start = Math.max(0, first - padding);
        int end = Math.min(sampleCount, last + padding + 1);
        if (end - start < minimumSamples) {
            int deficit = minimumSamples - (end - start);
            start = Math.max(0, start - deficit / 2);
            end = Math.min(sampleCount, end + deficit - (start == 0 ? 0 : deficit / 2));
        }
        if (end - start < minimumSamples) {
            return null;
        }
        if (end - start > maximumSamples) {
            int center = (first + last) / 2;
            start = Math.max(0, center - maximumSamples / 2);
            end = Math.min(sampleCount, start + maximumSamples);
            start = Math.max(0, end - maximumSamples);
        }

        int byteStart = start * 2;
        int byteLength = (end - start) * 2;
        byte[] result = new byte[byteLength];
        System.arraycopy(pcm, byteStart, result, 0, byteLength);
        return result;
    }
    private void releaseSystemSpeechRecognizer() {
        activeSystemSessionId = null;
        if (speechRecognizer != null) {
            speechRecognizer.cancel();
            speechRecognizer.destroy();
            speechRecognizer = null;
        }
    }

    private String offlineUnavailableMessage() {
        if (offlineModelLoading) {
            return "离线语音模型正在准备，请稍后重试。";
        }
        if (offlineInitError != null && offlineInitError.length() > 0) {
            return offlineInitError;
        }
        return "当前手机没有可用的语音识别服务，离线语音模型也不可用。";
    }

    private String extractVoskText(String hypothesis) throws JSONException {
        if (hypothesis == null) {
            throw new JSONException("离线模型未返回识别结果。");
        }

        return new JSONObject(hypothesis).optString("text", "").trim();
    }
    private String safeThrowableMessage(Throwable throwable) {
        return throwable == null || throwable.getMessage() == null || throwable.getMessage().length() == 0
            ? "未知错误。"
            : throwable.getMessage();
    }
    private String safeExceptionMessage(Exception exception) {
        return exception == null || exception.getMessage() == null || exception.getMessage().length() == 0
            ? "未知错误。"
            : exception.getMessage();
    }
    private SpeechRecognizer createSpeechRecognizer(boolean preferOnDevice) {
        usingOnDeviceRecognizer = false;
        if (preferOnDevice && isOnDeviceRecognitionAvailable()) {
            try {
                usingOnDeviceRecognizer = true;
                return SpeechRecognizer.createOnDeviceSpeechRecognizer(activity);
            } catch (Exception ignored) {
                usingOnDeviceRecognizer = false;
            }
        }

        return SpeechRecognizer.createSpeechRecognizer(activity);
    }

    private boolean isOnDeviceRecognitionAvailable() {
        return activity != null
            && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S
            && SpeechRecognizer.isOnDeviceRecognitionAvailable(activity);
    }

    private String normalizeLanguage(String languageTag) {
        if (languageTag == null || languageTag.length() == 0 || languageTag.equals("zh")) {
            return "zh-CN";
        }

        return languageTag.replace('_', '-');
    }

    private Locale localeFromTag(String languageTag) {
        String normalized = normalizeLanguage(languageTag);
        if (normalized.equalsIgnoreCase("zh-CN")) {
            return Locale.CHINA;
        }

        if (normalized.equalsIgnoreCase("en-US")) {
            return Locale.US;
        }

        String[] parts = normalized.split("-");
        if (parts.length >= 2) {
            return new Locale(parts[0], parts[1]);
        }

        return new Locale(normalized);
    }

    private void send(String callbackId, String method, String message) {
        UnityPlayer.UnitySendMessage(unityObjectName, method, (callbackId == null ? "" : callbackId) + "\n" + (message == null ? "" : message));
    }

    private void sendSpeechResult(String sessionId, String backend, long speechEndedAt, long recognizedAt, String text) {
        send(sessionId, "OnAndroidSpeechResult", backend + "\t" + speechEndedAt + "\t" + recognizedAt + "\n" + text);
    }

    private final class UnityRecognitionListener implements RecognitionListener {
        private final String sessionId;
        private String lastPartialResult = "";
        private long speechEndedAt = -1;

        UnityRecognitionListener(String sessionId) {
            this.sessionId = sessionId;
        }

        @Override
        public void onReadyForSpeech(Bundle params) {
            if (sessionId.equals(activeSystemSessionId)) {
                send(sessionId, "OnAndroidSpeechReady", "");
            }
        }
        @Override public void onBeginningOfSpeech() { }
        @Override public void onRmsChanged(float rmsdB) { }
        @Override public void onBufferReceived(byte[] buffer) { }
        @Override public void onEndOfSpeech() {
            if (sessionId.equals(activeSystemSessionId)) {
                speechEndedAt = SystemClock.elapsedRealtime();
            }
        }

        @Override
        public void onError(int error) {
            if (!sessionId.equals(activeSystemSessionId)) {
                return;
            }

            activeSystemSessionId = null;
            // Some recognizers emit a partial transcript, then end with an
            // error instead of onResults. Retain that real user speech.
            if (lastPartialResult.length() > 0) {
                sendSpeechResult(sessionId, "android", speechEndedAt, SystemClock.elapsedRealtime(), lastPartialResult);
                return;
            }
            if ((error == SpeechRecognizer.ERROR_NETWORK || error == SpeechRecognizer.ERROR_NETWORK_TIMEOUT)
                && startOfflineRecognition(sessionId)) {
                return;
            }
            send(sessionId, error == SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS
                ? "OnAndroidSpeechPermissionError" : "OnAndroidSpeechError", errorMessage(error));
        }

        @Override
        public void onResults(Bundle results) {
            if (!sessionId.equals(activeSystemSessionId)) {
                return;
            }

            activeSystemSessionId = null;
            long recognizedAt = SystemClock.elapsedRealtime();
            ArrayList<String> matches = results == null ? null : results.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION);
            if (matches != null && matches.size() > 0 && matches.get(0) != null && matches.get(0).trim().length() > 0) {
                sendSpeechResult(sessionId, "android", speechEndedAt, recognizedAt, matches.get(0).trim());
                return;
            }
            if (lastPartialResult.length() > 0) {
                sendSpeechResult(sessionId, "android", speechEndedAt, recognizedAt, lastPartialResult);
                return;
            }
            send(sessionId, "OnAndroidSpeechError", "没有识别到语音内容。");
        }

        @Override
        public void onPartialResults(Bundle partialResults) {
            ArrayList<String> matches = partialResults == null ? null : partialResults.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION);
            if (matches != null && matches.size() > 0 && matches.get(0) != null && matches.get(0).trim().length() > 0) {
                lastPartialResult = matches.get(0).trim();
            }
        }

        @Override public void onEvent(int eventType, Bundle params) { }
    }
    private String errorMessage(int error) {
        switch (error) {
            case SpeechRecognizer.ERROR_AUDIO:
                return "录音失败。";
            case SpeechRecognizer.ERROR_CLIENT:
                return "语音识别客户端错误。";
            case SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS:
                return "没有麦克风权限。";
            case SpeechRecognizer.ERROR_NETWORK:
            case SpeechRecognizer.ERROR_NETWORK_TIMEOUT:
                return usingOnDeviceRecognizer
                    ? "设备端语音识别不可用，请在系统设置下载中文离线语音包后重试。"
                    : "系统语音识别网络失败，请确认系统语音服务可联网，或下载中文离线语音包。";
            case SpeechRecognizer.ERROR_NO_MATCH:
                return "没有识别到语音内容。";
            case SpeechRecognizer.ERROR_RECOGNIZER_BUSY:
                return "系统语音识别正忙，请稍后再试。";
            case SpeechRecognizer.ERROR_SERVER:
                return "系统语音识别服务异常。";
            case SpeechRecognizer.ERROR_SPEECH_TIMEOUT:
                return "没有听到说话。";
            default:
                return "系统语音识别失败：" + error;
        }
    }
}
