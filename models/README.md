# Local model files

Put a `.gguf` model file in this folder to run extraction on a local LLM through llama.cpp (`llama-server`). Model files are git-ignored and kept out of Docker build contexts.

1. Download a GGUF model that follows instructions well, for example [Qwen2.5 3B Instruct Q4_K_M](https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/blob/main/qwen2.5-3b-instruct-q4_k_m.gguf) (2.1 GB) or the 7B version for better accuracy (4.7 GB), into this folder.
2. In `.env`, set:

   ```text
   LLAMA_MODEL_FILE=qwen2.5-3b-instruct-q4_k_m.gguf
   AI_LOCAL_PROVIDER=OpenAI
   AI_LOCAL_ENDPOINT=http://llamacpp:8080/v1/
   AI_LOCAL_MODEL=local-model
   ```

3. Start the model server, then the app:

   ```bash
   docker compose --profile llamacpp up -d
   ```

4. In the web UI, choose **Local** in the "AI model for new uploads" dropdown.

The server runs on the CPU, so it works the same on macOS, Linux and Windows. Expect tens of seconds to several minutes per invoice depending on the model size and your CPU; raise `AI_LOCAL_ATTEMPT_TIMEOUT_SECONDS` / `AI_LOCAL_TOTAL_TIMEOUT_SECONDS` if extractions time out. The server is also reachable from your machine at `http://localhost:8082/v1/` (`LLAMACPP_PORT`), for example when running the API and worker with `dotnet run`.

See [Configuration](../docs/wiki/Configuration.md#cloud-and-local-models) for running a model server outside Docker instead.
