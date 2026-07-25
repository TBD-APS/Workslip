#!/usr/bin/env python3
"""Headless browser smoke tests for the isolated Workslip stack."""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
from pathlib import Path

from selenium import webdriver
from selenium.common.exceptions import TimeoutException
from selenium.webdriver.common.by import By
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.support.ui import WebDriverWait


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://127.0.0.1:5270")
    parser.add_argument("--artifacts", default="artifacts/selenium")
    return parser.parse_args()


def save_diagnostics(driver: webdriver.Chrome, artifacts: Path, name: str) -> None:
    artifacts.mkdir(parents=True, exist_ok=True)
    driver.save_screenshot(str(artifacts / f"{name}.png"))
    (artifacts / f"{name}.html").write_text(driver.page_source, encoding="utf-8")
    try:
        logs = driver.get_log("browser")
    except Exception:  # Browser logging is best-effort diagnostics.
        logs = []
    (artifacts / f"{name}-browser.json").write_text(json.dumps(logs, indent=2), encoding="utf-8")


def main() -> int:
    args = parse_args()
    artifacts = Path(args.artifacts)

    options = webdriver.ChromeOptions()
    options.add_argument("--headless=new")
    options.add_argument("--no-sandbox")
    options.add_argument("--disable-dev-shm-usage")
    options.add_argument("--window-size=1440,1000")
    options.set_capability("goog:loggingPrefs", {"browser": "ALL"})

    driver = webdriver.Chrome(options=options)
    wait = WebDriverWait(driver, 30)

    try:
        driver.get(f"{args.base_url}/login")
        wait.until(EC.text_to_be_present_in_element((By.TAG_NAME, "h2"), "Log ind på Workslip"))

        admin_button = wait.until(
            EC.element_to_be_clickable((By.XPATH, "//button[normalize-space()='Dev Login · Admin']"))
        )
        admin_button.click()

        wait.until(lambda browser: "/app" in browser.current_url)
        wait.until(
            lambda browser: bool(
                browser.execute_script("return window.localStorage.getItem('authToken')")
                or browser.execute_script("return window.sessionStorage.getItem('authToken')")
            )
        )
        wait.until_not(EC.text_to_be_present_in_element((By.TAG_NAME, "body"), "Log ind på Workslip"))

        driver.get(f"{args.base_url}/app/customers")
        wait.until(EC.text_to_be_present_in_element((By.TAG_NAME, "h2"), "Kunder"))
        wait.until_not(EC.text_to_be_present_in_element((By.TAG_NAME, "body"), "Kunne ikke hente kunder"))

        customer_count_text = wait.until(
            EC.visibility_of_element_located((By.CSS_SELECTOR, ".page-header .subtitle"))
        ).text
        if "kunde" not in customer_count_text:
            raise AssertionError(f"Unexpected customer count text: {customer_count_text!r}")

        driver.get(f"{args.base_url}/app/customers/new")
        wait.until(EC.text_to_be_present_in_element((By.TAG_NAME, "body"), "Opret kunde"))

        save_diagnostics(driver, artifacts, "success")
        print("Selenium smoke tests passed: dev login, customer list and create-customer route.")
        return 0
    except Exception as exc:
        save_diagnostics(driver, artifacts, "failure")
        print(f"Selenium smoke tests failed: {exc}", file=sys.stderr)
        return 1
    finally:
        driver.quit()


if __name__ == "__main__":
    raise SystemExit(main())
