FROM mariadb:10.11

# Copy initialization scripts
# These scripts will run automatically when the container is created for the first time
COPY ./COMPLETE_DATABASE_SETUP.sql /docker-entrypoint-initdb.d/
COPY ./stored_procedures.sql /docker-entrypoint-initdb.d/

# Expose MySQL/MariaDB port
EXPOSE 3306
