package com.srtp.avatar;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.speech.RecognitionListener;
import android.speech.RecognizerIntent;
import android.speech.SpeechRecognizer;
import android.speech.tts.TextToSpeech;
import android.speech.tts.UtteranceProgressListener;

import com.unity3d.player.UnityPlayer;

import java.util.ArrayList;
import java.util.Locale;

public final class NativeSpeechBridge {
    private final Activity activity;
    private final String unityObjectName;
    private SpeechRecognizer speechRecognizer;
    private TextToSpeech textToSpeech;
    private boolean ttsReady;

    public NativeSpeechBridge(Activity activity, String unityObjectName) {
        this.activity = activity;
        this.unityObjectName = unityObjectName;
        initializeTextToSpeech();
    }

    public boolean isSpeechRecognitionAvailable() {
        return activity != null && SpeechRecognizer.isRecognitionAvailable(activity);
    }

    public boolean isTextToSpeechAvailable() {
        return activity != null && textToSpeech != null && ttsReady;
    }

    public void startRecognition(final String languageTag, final String sessionId) {
        if (activity == null) {
            send(sessionId, "OnAndroidSpeechError", "Android Activity 不可用。");
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    if (!SpeechRecognizer.isRecognitionAvailable(activity)) {
                        send(sessionId, "OnAndroidSpeechError", "当前手机没有可用的系统语音识别服务。");
                        return;
                    }

                    if (speechRecognizer != null) {
                        speechRecognizer.destroy();
                    }

                    speechRecognizer = SpeechRecognizer.createSpeechRecognizer(activity);
                    speechRecognizer.setRecognitionListener(new UnityRecognitionListener(sessionId));

                    Intent intent = new Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH);
                    intent.putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM);
                    intent.putExtra(RecognizerIntent.EXTRA_LANGUAGE, normalizeLanguage(languageTag));
                    intent.putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, false);
                    intent.putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 3);
                    // These are advisory values, but on supported recognizers they make a
                    // spoken turn finish promptly without requiring the user to tap again.
                    intent.putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_MINIMUM_LENGTH_MILLIS, 500);
                    intent.putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_POSSIBLY_COMPLETE_SILENCE_LENGTH_MILLIS, 900);
                    intent.putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS, 1100);
                    speechRecognizer.startListening(intent);
                } catch (Exception ex) {
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
                if (speechRecognizer != null) {
                    speechRecognizer.cancel();
                    speechRecognizer.destroy();
                    speechRecognizer = null;
                }
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
                if (speechRecognizer != null) {
                    speechRecognizer.destroy();
                    speechRecognizer = null;
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
            send(sessionId, "OnAndroidSpeechError", errorMessage(error));
        }

        @Override
        public void onResults(Bundle results) {
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
                return "系统语音识别网络失败。";
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
