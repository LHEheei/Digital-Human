package com.srtp.avatar;

import android.app.Activity;
import android.content.Intent;
import android.os.Build;
import android.os.Bundle;
import android.speech.RecognitionListener;
import android.speech.RecognizerIntent;
import android.speech.SpeechRecognizer;
import android.speech.tts.TextToSpeech;
import android.speech.tts.UtteranceProgressListener;

import com.unity3d.player.UnityPlayer;

import org.json.JSONObject;
import org.vosk.Model;
import org.vosk.Recognizer;
import org.vosk.android.StorageService;

import java.util.ArrayList;
import java.util.Locale;

public final class NativeSpeechBridge {
    private final Activity activity;
    private final String unityObjectName;
    private SpeechRecognizer speechRecognizer;
    private TextToSpeech textToSpeech;
    private boolean ttsReady;
    private boolean usingOnDeviceRecognizer;
    private String activeSystemSessionId;

    private static final String OFFLINE_MODEL_ASSET = "vosk-model-small-cn-0.22";
    private static final String OFFLINE_MODEL_STORAGE = "srtp-vosk-model";
    private static final float OFFLINE_SAMPLE_RATE = 16000.0f;
    private static final int OFFLINE_LISTEN_TIMEOUT_MILLIS = 15000;

    private Model offlineModel;
    private Recognizer offlineRecognizer;
    private org.vosk.android.SpeechService offlineSpeechService;
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
                    intent.putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, false);
                    intent.putExtra(RecognizerIntent.EXTRA_PREFER_OFFLINE, usingOnDeviceRecognizer);
                    intent.putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 3);
                    // These are advisory values, but on supported recognizers they make a
                    // spoken turn finish promptly without requiring the user to tap again.
                    intent.putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_MINIMUM_LENGTH_MILLIS, 500);
                    intent.putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_POSSIBLY_COMPLETE_SILENCE_LENGTH_MILLIS, 900);
                    intent.putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS, 1100);
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
                    int result = textToSpeech.speak(text == null || text.length() == 0 ? "好的。" : text, TextToSpeech.QUEUE_FLUSH, params, utteranceId);
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
                        public void onStart(String utteranceId) {
                            send(utteranceId, "OnAndroidTtsStarted", "");
                        }

                        @Override
                        public void onDone(String utteranceId) {
                            send(utteranceId, "OnAndroidTtsDone", "");
                        }

                        @Override
                        public void onError(String utteranceId) {
                            send(utteranceId, "OnAndroidTtsError", "系统 TTS 播报失败。");
                        }
                    });
                }
            }
        });
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
            offlineRecognizer.setEndpointerDelays(5.0f, 0.8f, 15.0f);
            offlineSpeechService = new org.vosk.android.SpeechService(offlineRecognizer, OFFLINE_SAMPLE_RATE);
            offlineSessionId = sessionId;
            if (!offlineSpeechService.startListening(new UnityOfflineRecognitionListener(sessionId), OFFLINE_LISTEN_TIMEOUT_MILLIS)) {
                stopOfflineRecognition();
                return false;
            }
            return true;
        } catch (Exception ex) {
            offlineInitError = "离线语音识别启动失败：" + safeExceptionMessage(ex);
            stopOfflineRecognition();
            return false;
        }
    }

    private void stopOfflineRecognition() {
        offlineSessionId = null;
        if (offlineSpeechService != null) {
            offlineSpeechService.cancel();
            offlineSpeechService.shutdown();
            offlineSpeechService = null;
        }
        if (offlineRecognizer != null) {
            offlineRecognizer.close();
            offlineRecognizer = null;
        }
    }

    private void finishOfflineRecognition(String sessionId, String text, String error) {
        if (offlineSessionId == null || !offlineSessionId.equals(sessionId)) {
            return;
        }

        stopOfflineRecognition();
        if (error != null) {
            send(sessionId, "OnAndroidSpeechError", error);
        } else if (text == null || text.trim().length() == 0) {
            send(sessionId, "OnAndroidSpeechError", "没有识别到语音内容。");
        } else {
            send(sessionId, "OnAndroidSpeechResult", text.trim());
        }
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

    private String extractVoskText(String hypothesis) {
        try {
            return new JSONObject(hypothesis == null ? "" : hypothesis).optString("text", "").trim();
        } catch (Exception ignored) {
            return "";
        }
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

    private final class UnityRecognitionListener implements RecognitionListener {
        private final String sessionId;

        UnityRecognitionListener(String sessionId) {
            this.sessionId = sessionId;
        }

        @Override
        public void onReadyForSpeech(Bundle params) {
        }

        @Override
        public void onBeginningOfSpeech() {
        }

        @Override
        public void onRmsChanged(float rmsdB) {
        }

        @Override
        public void onBufferReceived(byte[] buffer) {
        }

        @Override
        public void onEndOfSpeech() {
        }

        @Override
        public void onError(int error) {
            if (!sessionId.equals(activeSystemSessionId)) {
                return;
            }

            activeSystemSessionId = null;
            if ((error == SpeechRecognizer.ERROR_NETWORK || error == SpeechRecognizer.ERROR_NETWORK_TIMEOUT)
                && startOfflineRecognition(sessionId)) {
                return;
            }
            send(sessionId, "OnAndroidSpeechError", errorMessage(error));
        }

        @Override
        public void onResults(Bundle results) {
            if (!sessionId.equals(activeSystemSessionId)) {
                return;
            }

            activeSystemSessionId = null;
            ArrayList<String> matches = results == null ? null : results.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION);
            if (matches == null || matches.size() == 0) {
                send(sessionId, "OnAndroidSpeechError", "没有识别到语音内容。");
                return;
            }

            send(sessionId, "OnAndroidSpeechResult", matches.get(0));
        }

        @Override
        public void onPartialResults(Bundle partialResults) {
        }

        @Override
        public void onEvent(int eventType, Bundle params) {
        }
    }

    private final class UnityOfflineRecognitionListener implements org.vosk.android.RecognitionListener {
        private final String sessionId;

        UnityOfflineRecognitionListener(String sessionId) {
            this.sessionId = sessionId;
        }

        @Override
        public void onPartialResult(String hypothesis) {
        }

        @Override
        public void onResult(String hypothesis) {
            String text = extractVoskText(hypothesis);
            if (text.length() > 0) {
                finishOfflineRecognition(sessionId, text, null);
            }
        }

        @Override
        public void onFinalResult(String hypothesis) {
            finishOfflineRecognition(sessionId, extractVoskText(hypothesis), null);
        }

        @Override
        public void onError(Exception exception) {
            finishOfflineRecognition(sessionId, null, "离线语音识别失败：" + safeExceptionMessage(exception));
        }

        @Override
        public void onTimeout() {
            finishOfflineRecognition(sessionId, null, "没有听到说话。" );
        }
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
