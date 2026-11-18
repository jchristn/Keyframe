# vLLM with the audio extras Voxtral needs. The official image ships mistral-common without its audio dependencies
# (soundfile, soxr), so Voxtral fails at startup with "soundfile is not installed". Verified on vLLM v0.33.0.
FROM vllm/vllm-openai:v0.33.0
RUN pip install --no-cache-dir "mistral-common[audio]"
