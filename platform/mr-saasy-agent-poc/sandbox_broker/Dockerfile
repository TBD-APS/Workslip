FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

WORKDIR /broker

RUN python -m pip install --no-cache-dir 'docker>=7,<8'

COPY sandbox_broker/server.py /broker/server.py

EXPOSE 8080

CMD ["python", "/broker/server.py"]
